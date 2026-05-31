using Microsoft.Extensions.Logging;
using System.Reflection;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Infrastructure;

namespace Cocoar.Configuration.Reactive;

internal static partial class ReactiveConfigurationFactoryLog
{
    [LoggerMessage(EventId = 6400, Level = LogLevel.Warning, Message = "Failed to locate GetReactiveConfig for type {Type}")]
    public static partial void MissingGetReactiveConfig(this ILogger logger, Type Type);

    [LoggerMessage(EventId = 6401, Level = LogLevel.Warning, Message = "Failed to locate GetConfig for type {Type}")]
    public static partial void MissingGetConfig(this ILogger logger, Type Type);

    [LoggerMessage(EventId = 6402, Level = LogLevel.Warning, Message = "Failed to prime reactive configuration for tuple element {Type}")]
    public static partial void PrimeReactiveConfigFailed(this ILogger logger, Exception exception, Type Type);

    [LoggerMessage(EventId = 6403, Level = LogLevel.Warning, Message = "Type {Type} is not a class, skipping reactive priming")]
    public static partial void SkippingNonClassType(this ILogger logger, Type Type);
}

// `accessor` + backplaneAccessor (instead of a concrete ConfigManager) so a tenant pipeline builds its
// reactive configs over ITS OWN accessor/backplane (ADR-005 §7). The global pipeline passes the owning
// ConfigManager as the accessor and that manager's backplane — byte-identical to before.
// NOTE: this field is intentionally NOT named `configAccessor` — several methods below take a local
// `Func<T> configAccessor` (the value closure), which would shadow it and silently mis-bind the delegate.
internal class ReactiveConfigurationFactory(
    ReactiveConfigManager reactiveConfigManager,
    List<ConfigRule> rules,
    ILogger logger,
    IConfigurationAccessor accessor,
    Func<MasterBackplane> backplaneAccessor,
    ExposureRegistry bindingRegistry)
{
    private static readonly MethodInfo _getReactiveConfigMethod =
        typeof(ReactiveConfigManager).GetMethod(nameof(ReactiveConfigManager.GetReactiveConfig))!;
    private static readonly MethodInfo _getConfigMethod =
        typeof(IConfigurationAccessor).GetMethod(nameof(IConfigurationAccessor.GetConfig), Type.EmptyTypes)!;

    public IReactiveConfig<T> GetReactiveConfig<T>(Func<T> configAccessor)
    {
        var t = typeof(T);
        if (IsValueTupleType(t))
        {
            return (IReactiveConfig<T>)CreateTupleReactiveConfig(t);
        }

        // For interfaces, look up the concrete type from the binding registry
        if (t.IsInterface)
        {
            if (!bindingRegistry.TryGetConcreteType(t, out var concreteType))
            {
                throw new InvalidOperationException(
                    $"GetReactiveConfig<{t.Name}> requires the interface to be exposed via " +
                    $"setup.ConcreteType<T>().ExposeAs<{t.Name}>(). No concrete type mapping found.");
            }

            // Use the concrete type for the reactive config, but wrap the accessor
            return CreateReactiveConfigForConcreteType<T>(concreteType, configAccessor);
        }

        // For non-tuple, non-interface types, must be a class for the backplane
        if (!t.IsClass)
        {
            throw new InvalidOperationException(
                $"GetReactiveConfig<{t.Name}> is only supported for class types, interfaces (with ExposeAs), or ValueTuple types. " +
                $"Configuration types should be classes, not structs.");
        }

        // Use reflection to call the generic method with class constraint
        var method = _getReactiveConfigMethod.MakeGenericMethod(t);

        var funcType = typeof(Func<>).MakeGenericType(t);
        return (IReactiveConfig<T>)method.Invoke(reactiveConfigManager, [configAccessor])!;
    }

    /// <summary>
    /// Creates a reactive config for an interface type by using the concrete type's reactive config.
    /// The concrete type implements the interface, so we can cast safely.
    /// </summary>
    private IReactiveConfig<TInterface> CreateReactiveConfigForConcreteType<TInterface>(Type concreteType, Func<TInterface> configAccessor)
    {
        // Create an accessor for the concrete type that returns TInterface (which the concrete type implements)
        var concreteAccessorMethod = _getConfigMethod.MakeGenericMethod(concreteType);

        var concreteFuncType = typeof(Func<>).MakeGenericType(concreteType);
        var concreteAccessor = Delegate.CreateDelegate(concreteFuncType, accessor, concreteAccessorMethod);

        // Get the reactive config for the concrete type
        var reactiveMethod = _getReactiveConfigMethod.MakeGenericMethod(concreteType);

        var concreteReactiveConfig = reactiveMethod.Invoke(reactiveConfigManager, [concreteAccessor])!;

        // Wrap the concrete reactive config in an interface adapter
        var adapterType = typeof(InterfaceReactiveConfigAdapter<,>).MakeGenericType(typeof(TInterface), concreteType);
        return (IReactiveConfig<TInterface>)Activator.CreateInstance(adapterType, concreteReactiveConfig)!;
    }

    private static bool IsValueTupleType(Type t) =>
        t is { IsValueType: true, FullName: not null } && t.FullName.StartsWith("System.ValueTuple", StringComparison.Ordinal);

    private object CreateTupleReactiveConfig(Type tupleType)
    {
        var elementTypes = FlattenTuple(tupleType).ToArray();
        if (elementTypes.Length == 0)
        {
            throw new InvalidOperationException($"Type {tupleType.Name} is not a non-empty ValueTuple");
        }

        var allowedConcrete = new HashSet<Type>(rules.Select(r => r.ConcreteType));

        var invalid = new List<string>();

        foreach (var et in elementTypes)
        {
            if (et.IsInterface)
            {
                if (!bindingRegistry.TryGetConcreteType(et, out _))
                {
                    invalid.Add(et.Name + " (interface not exposed)");
                }
            }
            else
            {
                if (!allowedConcrete.Contains(et))
                {
                    invalid.Add(et.Name + " (not a configured type)");
                }
            }
        }

        if (invalid.Count > 0)
        {
            throw new InvalidOperationException($"Cannot create IReactiveConfig<{tupleType.Name}>. The following tuple element types are not configured/exposed: {string.Join(", ", invalid)}");
        }

        // In the GLOBAL pipeline (no tenant), a type whose EVERY rule is .TenantScoped() has no global value —
        // its rules skip when there is no tenant. Surface that precisely instead of the generic "Missing
        // configuration" from the tuple ctor; the fix is to read the tuple per tenant. (Mixed-scope tuples are
        // otherwise fully supported: each element comes from this pipeline's snapshot.)
        if (string.IsNullOrEmpty(accessor.Tenant))
        {
            var tenantScopedOnly = new List<string>();
            foreach (var et in elementTypes.Distinct())
            {
                var concreteEt = et;
                if (et.IsInterface && bindingRegistry.TryGetConcreteType(et, out var ct))
                {
                    concreteEt = ct;
                }

                var typeRules = rules.Where(r => r.ConcreteType == concreteEt).ToList();
                if (typeRules.Count > 0 && typeRules.All(r => r.Options?.TenantScoped == true))
                {
                    tenantScopedOnly.Add(et.Name);
                }
            }

            if (tenantScopedOnly.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot create IReactiveConfig<{tupleType.Name}> in the global pipeline: type(s) " +
                    $"{string.Join(", ", tenantScopedOnly)} have only .TenantScoped() rules, so they have no " +
                    $"global value. Use GetReactiveConfigForTenant<{tupleType.Name}>(tenantId) instead.");
            }
        }

        // Prime each distinct element type's reactive config
        foreach (var et in elementTypes.Distinct())
        {
            // For interfaces, resolve to concrete type for priming
            var typeToPrime = et;
            if (et.IsInterface)
            {
                if (bindingRegistry.TryGetConcreteType(et, out var concreteType))
                {
                    typeToPrime = concreteType;
                }
                else
                {
                    logger.SkippingNonClassType(et);
                    continue;
                }
            }
            else if (!et.IsClass)
            {
                // Skip non-class, non-interface types (structs)
                logger.SkippingNonClassType(et);
                continue;
            }

            try
            {
                var reactiveMethod = _getReactiveConfigMethod.MakeGenericMethod(typeToPrime);
                var accessorMethod = _getConfigMethod.MakeGenericMethod(typeToPrime);

                var funcType = typeof(Func<>).MakeGenericType(typeToPrime);
                var accessorDelegate = Delegate.CreateDelegate(funcType, accessor, accessorMethod);

                _ = reactiveMethod.Invoke(reactiveConfigManager, [accessorDelegate]);
            }
            catch (Exception ex)
            {
                logger.PrimeReactiveConfigFailed(ex, typeToPrime);
            }
        }

        var generic = typeof(ReactiveTupleConfig<>).MakeGenericType(tupleType);
        return Activator.CreateInstance(generic, accessor, backplaneAccessor(), reactiveConfigManager, logger, bindingRegistry)!;
    }

    private static IEnumerable<Type> FlattenTuple(Type t)
    {
        if (!(t is { IsValueType: true, FullName: not null } && t.FullName.StartsWith("System.ValueTuple", StringComparison.Ordinal)))
        {
            yield break;
        }

        var fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (f is { Name: "Rest", FieldType.FullName: not null } && f.FieldType.FullName.StartsWith("System.ValueTuple", StringComparison.Ordinal))
            {
                foreach (var inner in FlattenTuple(f.FieldType))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return f.FieldType;
            }
        }
    }
}

/// <summary>
/// Adapts an IReactiveConfig of a concrete type to an IReactiveConfig of an interface type.
/// Used when injecting IReactiveConfig&lt;IInterface&gt; where IInterface is exposed via ExposeAs.
/// </summary>
internal sealed class InterfaceReactiveConfigAdapter<TInterface, TConcrete> : IReactiveConfig<TInterface>
    where TConcrete : class, TInterface
{
    private readonly IReactiveConfig<TConcrete> _inner;

    public InterfaceReactiveConfigAdapter(IReactiveConfig<TConcrete> inner)
    {
        _inner = inner;
    }

    public TInterface CurrentValue => _inner.CurrentValue;

    public IDisposable Subscribe(IObserver<TInterface> observer)
    {
        // Adapt the observer to accept TConcrete (which is assignable to TInterface)
        return _inner.Subscribe(new CastingObserver<TInterface, TConcrete>(observer));
    }

    private sealed class CastingObserver<TOut, TIn>(IObserver<TOut> inner) : IObserver<TIn>
        where TIn : TOut
    {
        public void OnCompleted() => inner.OnCompleted();
        public void OnError(Exception error) => inner.OnError(error);
        public void OnNext(TIn value) => inner.OnNext(value);
    }
}
