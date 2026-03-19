using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Builder for registering <see cref="IContextResolver{TRequest,TContext}"/> implementations
/// using collection expressions. Used as the second parameter to the DI-layer
/// <c>UseFeatureFlags</c>/<c>UseEntitlements</c> overloads.
/// </summary>
/// <example>
/// <code>
/// .UseFeatureFlags(
///     flags => [flags.Register&lt;BillingFlags&gt;()],
///     resolvers => [
///         resolvers.Global&lt;UserByIdResolver&gt;(),
///         resolvers.For&lt;BillingFlags&gt;(r => r
///             .Use&lt;BillingResolver&gt;()
///             .ForProperty(f => f.BetaCheckout).Use&lt;BetaResolver&gt;())
///     ])
/// </code>
/// </example>
public sealed class ResolverBuilder
{
    /// <summary>
    /// Registers a global context resolver that acts as the final fallback for all
    /// contextual flag/entitlement properties across every registered class,
    /// when no property-level or class-level resolver matches.
    /// </summary>
    /// <typeparam name="TResolver">
    /// A type that implements <see cref="IContextResolver{TRequest,TContext}"/>.
    /// </typeparam>
    public ResolverRegistration Global<TResolver>()
        where TResolver : class
    {
        var (requestType, contextType) = ExtractResolverTypes(typeof(TResolver));
        var registration = new ContextResolverRegistration(typeof(TResolver), requestType, contextType, null);
        return new ResolverRegistration(registration);
    }

    /// <summary>
    /// Configures class-level and property-level context resolvers for a specific
    /// flag or entitlement class.
    /// </summary>
    /// <typeparam name="TFlags">The flag or entitlement class to configure resolvers for.</typeparam>
    /// <param name="configure">
    /// Delegate that configures class-level and property-level resolvers via a
    /// <see cref="ClassResolverBuilder{T}"/>.
    /// </param>
    public ResolverRegistration For<TFlags>(Action<ClassResolverBuilder<TFlags>> configure)
        where TFlags : class
    {
        ArgumentNullException.ThrowIfNull(configure);

        var classBuilder = new ClassResolverBuilder<TFlags>();
        configure(classBuilder);
        var resolvers = classBuilder.Build();

        return new ResolverRegistration(resolvers, typeof(TFlags));
    }

    internal static (Type RequestType, Type ContextType) ExtractResolverTypes(Type resolverType)
    {
        var iface = resolverType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContextResolver<,>))
            ?? throw new ArgumentException(
                $"{resolverType.Name} does not implement IContextResolver<TRequest, TContext>.",
                nameof(resolverType));

        return (iface.GenericTypeArguments[0], iface.GenericTypeArguments[1]);
    }
}
