using Microsoft.Extensions.Logging;
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

        // For non-tuple types, must be a class for the backplane
        if (!t.IsClass)
        {
            throw new InvalidOperationException(
                $"GetReactiveConfig<{t.Name}> is only supported for class types or ValueTuple types. " +
                $"Configuration types should be classes, not structs.");
        }

        // Use reflection to call the generic method with class constraint
        var method = typeof(ReactiveConfigManager)
            .GetMethod(nameof(ReactiveConfigManager.GetReactiveConfig))?
            .MakeGenericMethod(t);

        if (method == null)
        {
            throw new InvalidOperationException($"Cannot create IReactiveConfig<{t.Name}> - internal error.");
        }

        var funcType = typeof(Func<>).MakeGenericType(t);
        return (IReactiveConfig<T>)method.Invoke(reactiveConfigManager, [configAccessor])!;
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

        // Prime each distinct element type's reactive config
        foreach (var et in elementTypes.Distinct())
        {
            // Only prime reference types (class constraint on ReactiveConfigManager)
            if (!et.IsClass)
            {
                logger.SkippingNonClassType(et);
                continue;
            }

            try
            {
                var reactiveMethod = typeof(ReactiveConfigManager)
                    .GetMethod(nameof(ReactiveConfigManager.GetReactiveConfig))?
                    .MakeGenericMethod(et);
                if (reactiveMethod == null)
                {
                    logger.MissingGetReactiveConfig(et);
                    continue;
                }

                var accessorMethod = typeof(ConfigManager)
                    .GetMethod(nameof(ConfigManager.GetConfig), Type.EmptyTypes)?
                    .MakeGenericMethod(et);
                if (accessorMethod == null)
                {
                    logger.MissingGetConfig(et);
                    continue;
                }

                var funcType = typeof(Func<>).MakeGenericType(et);
                var accessorDelegate = Delegate.CreateDelegate(funcType, configManager, accessorMethod);

                _ = reactiveMethod.Invoke(reactiveConfigManager, [accessorDelegate]);
            }
            catch (Exception ex)
            {
                logger.PrimeReactiveConfigFailed(ex, et);
            }
        }

        var generic = typeof(ReactiveTupleConfig<>).MakeGenericType(tupleType);
        return Activator.CreateInstance(generic, configManager, reactiveConfigManager, logger)!;
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
