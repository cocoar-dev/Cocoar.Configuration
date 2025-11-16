using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Infrastructure;

namespace Cocoar.Configuration.Reactive;

internal class ReactiveConfigurationFactory(
    ReactiveConfigManager reactiveConfigManager,
    List<ConfigRule> rules,
    ILogger logger,
    ConfigManager configManager,
    ExposureRegistry bindingRegistry)
{
    public IReactiveConfig<T> GetReactiveConfig<T>(Func<T> configAccessor)
    {
        var t = typeof(T);
        if (IsValueTupleType(t))
        {
            return (IReactiveConfig<T>)CreateTupleReactiveConfig(t);
        }
        return reactiveConfigManager.GetReactiveConfig(configAccessor);
    }

    private static bool IsValueTupleType(Type t) => 
        t is { IsValueType: true, FullName: not null } && t.FullName.StartsWith("System.ValueTuple");

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

        foreach (var et in elementTypes.Distinct())
        {
            try
            {
                var reactiveMethod = typeof(ReactiveConfigManager)
                    .GetMethod(nameof(ReactiveConfigManager.GetReactiveConfig))?
                    .MakeGenericMethod(et);
                if (reactiveMethod == null)
                {
                    logger.LogWarning("Failed to locate GetReactiveConfig for type {Type}", et);
                    continue;
                }

                var accessorMethod = typeof(ConfigManager)
                    .GetMethod(nameof(ConfigManager.GetConfig), Type.EmptyTypes)?
                    .MakeGenericMethod(et);
                if (accessorMethod == null)
                {
                    logger.LogWarning("Failed to locate GetConfig for type {Type}", et);
                    continue;
                }

                var funcType = typeof(Func<>).MakeGenericType(et);
                var accessorDelegate = Delegate.CreateDelegate(funcType, configManager, accessorMethod);

                _ = reactiveMethod.Invoke(reactiveConfigManager, [accessorDelegate]);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to prime reactive configuration for tuple element {Type}", et);
            }
        }

        var generic = typeof(ReactiveTupleConfig<>).MakeGenericType(tupleType);
        return Activator.CreateInstance(generic, configManager, reactiveConfigManager, logger)!;
    }

    private static IEnumerable<Type> FlattenTuple(Type t)
    {
        if (!(t is { IsValueType: true, FullName: not null } && t.FullName.StartsWith("System.ValueTuple")))
        {
            yield break;
        }

        var fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (f is { Name: "Rest", FieldType.FullName: not null } && f.FieldType.FullName.StartsWith("System.ValueTuple"))
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
