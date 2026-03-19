using System.Linq.Expressions;
using System.Reflection;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Pre-computed metadata for a single contextual flag property, built once at startup
/// from the resolver cascade and stored in <see cref="FlagsSetupData.EvaluationEntries"/>.
/// Includes compiled delegates that replace per-request reflection.
/// </summary>
/// <param name="FlagClassType">The <see cref="FeatureFlags"/> or <see cref="Entitlements"/> subclass that owns the property.</param>
/// <param name="Property">The <c>FeatureFlag&lt;TContext, TResult&gt;</c> property info.</param>
/// <param name="ContextType">The <c>TContext</c> type extracted from the flag property's generic args.</param>
/// <param name="Resolver">The winning resolver registration after cascade resolution.</param>
/// <param name="CompiledResolveAsync">
/// Pre-compiled delegate that calls <c>IContextResolver&lt;TRequest,TContext&gt;.ResolveAsync(request)</c>
/// on the resolver instance and returns the resolved context as <c>Task&lt;object?&gt;</c>.
/// Built once at startup to avoid per-request <see cref="Type.MakeGenericType"/> / reflection overhead.
/// </param>
/// <param name="CompiledFlagInvoke">
/// Pre-compiled delegate that reads the <c>FeatureFlag&lt;TContext,TResult&gt;</c> property from the flag class
/// instance, invokes it with the resolved context, and returns the result as <c>object?</c>.
/// Built once at startup to avoid per-request reflection overhead.
/// </param>
internal sealed record FlagEvaluationEntry(
    Type FlagClassType,
    PropertyInfo Property,
    Type ContextType,
    ContextResolverRegistration Resolver,
    Func<object, object, Task<object?>> CompiledResolveAsync,
    Func<object, object?, object?> CompiledFlagInvoke)
{
    /// <summary>
    /// Builds strongly-typed delegate wrappers for resolver invocation and flag evaluation.
    /// Uses a generic helper method so that the cast and call are baked into the delegate
    /// at construction time, eliminating all per-request reflection.
    /// </summary>
    internal static FlagEvaluationEntry Create(
        Type flagClassType,
        PropertyInfo property,
        Type contextType,
        ContextResolverRegistration resolver)
    {
        var resolveAsync = BuildResolveAsyncDelegate(resolver.RequestType, contextType);
        var flagInvoke = BuildFlagInvokeDelegate(flagClassType, property, contextType);

        return new FlagEvaluationEntry(
            flagClassType, property, contextType, resolver,
            resolveAsync, flagInvoke);
    }

    private static Func<object, object, Task<object?>> BuildResolveAsyncDelegate(
        Type requestType, Type contextType)
    {
        // Call the generic helper via reflection once at startup to produce a
        // strongly-typed lambda that runs without reflection at request time.
        var method = typeof(FlagEvaluationEntry)
            .GetMethod(nameof(CreateResolveAsyncDelegate), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(requestType, contextType);

        return (Func<object, object, Task<object?>>)method.Invoke(null, null)!;
    }

    private static Func<object, object, Task<object?>> CreateResolveAsyncDelegate<TRequest, TContext>()
    {
        return async (resolverInstance, request) =>
        {
            var typed = (IContextResolver<TRequest, TContext>)resolverInstance;
            var result = await typed.ResolveAsync((TRequest)request).ConfigureAwait(false);
            return result;
        };
    }

    /// <summary>
    /// Builds a compiled expression tree that: casts the flag class instance to its concrete type,
    /// reads the <c>FeatureFlag&lt;TContext, TResult&gt;</c> property, invokes the delegate with the
    /// cast context argument, and boxes the result to <c>object?</c>.
    /// </summary>
    private static Func<object, object?, object?> BuildFlagInvokeDelegate(
        Type flagClassType, PropertyInfo property, Type contextType)
    {
        // Parameters: (object flagClassInstance, object? context)
        var instanceParam = Expression.Parameter(typeof(object), "flagClassInstance");
        var contextParam = Expression.Parameter(typeof(object), "context");

        // (FlagClassType)flagClassInstance
        var castInstance = Expression.Convert(instanceParam, flagClassType);

        // ((FlagClassType)flagClassInstance).Property
        var propertyAccess = Expression.Property(castInstance, property);

        // (TContext)context
        var castContext = Expression.Convert(contextParam, contextType);

        // Invoke the FeatureFlag<TContext, TResult> delegate with the cast context
        var invokeCall = Expression.Invoke(propertyAccess, castContext);

        // Box the result to object
        var boxed = Expression.Convert(invokeCall, typeof(object));

        return Expression.Lambda<Func<object, object?, object?>>(
            boxed, instanceParam, contextParam).Compile();
    }
}
