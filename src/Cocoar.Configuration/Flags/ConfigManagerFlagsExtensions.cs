using System.Reflection;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags.Internal;
using Cocoar.Configuration.Reactive;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Extension methods on <see cref="ConfigManager"/> for resolving flag and entitlement
/// instances without a DI container. Instances are singletons — created once and cached
/// on the capability for the lifetime of the ConfigManager.
/// </summary>
public static class ConfigManagerFlagsExtensions
{
    private static readonly Type ReactiveConfigDef = typeof(IReactiveConfig<>);
    private static readonly MethodInfo GetReactiveConfigMethod =
        typeof(ConfigManager).GetMethod(nameof(ConfigManager.GetReactiveConfig))!;

    /// <summary>
    /// Resolves the singleton instance of the specified feature flag class.
    /// The instance is constructed once and cached for the lifetime of the manager.
    /// </summary>
    /// <typeparam name="T">A feature flag class registered via <c>UseFeatureFlags</c>.</typeparam>
    public static T GetFeatureFlags<T>(this ConfigManager manager) where T : class
    {
        var setup = manager.FlagsSetup
            ?? throw new InvalidOperationException(
                "UseFeatureFlags has not been configured. Call .UseFeatureFlags() in ConfigManager.Create().");

        return (T)setup.InstanceCache.GetOrAdd(typeof(T), _ => CreateInstance<T>(manager));
    }

    /// <summary>
    /// Resolves the singleton instance of the specified entitlement class.
    /// The instance is constructed once and cached for the lifetime of the manager.
    /// </summary>
    /// <typeparam name="T">An entitlement class registered via <c>UseEntitlements</c>.</typeparam>
    public static T GetEntitlements<T>(this ConfigManager manager) where T : class
    {
        var setup = manager.EntitlementsSetup
            ?? throw new InvalidOperationException(
                "UseEntitlements has not been configured. Call .UseEntitlements() in ConfigManager.Create().");

        return (T)setup.InstanceCache.GetOrAdd(typeof(T), _ => CreateInstance<T>(manager));
    }

    /// <summary>
    /// Constructs a flag or entitlement class instance by resolving its constructor parameters.
    /// Only <see cref="IReactiveConfig{T}"/> dependencies are supported — they are resolved
    /// from the ConfigManager. Parameterless constructors are also supported.
    /// </summary>
    private static T CreateInstance<T>(ConfigManager manager)
    {
        var type = typeof(T);

        // Prefer the constructor with the fewest parameters to handle common cases
        // where a parameterless ctor exists alongside injected ones.
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No public constructor found on '{type.Name}'.");

        var args = ctor.GetParameters()
            .Select(p => ResolveConstructorParameter(manager, p))
            .ToArray();

        return (T)ctor.Invoke(args);
    }

    private static object ResolveConstructorParameter(ConfigManager manager, ParameterInfo param)
    {
        var paramType = param.ParameterType;
        if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == ReactiveConfigDef)
        {
            var configType = paramType.GetGenericArguments()[0];
            return GetReactiveConfigMethod.MakeGenericMethod(configType).Invoke(manager, null)!;
        }

        throw new InvalidOperationException(
            $"Constructor parameter '{param.Name}' of type '{paramType.Name}' on '{param.Member.DeclaringType?.Name}' cannot be resolved. " +
            $"FeatureFlag and entitlement constructors may only depend on IReactiveConfig<T>.");
    }
}
