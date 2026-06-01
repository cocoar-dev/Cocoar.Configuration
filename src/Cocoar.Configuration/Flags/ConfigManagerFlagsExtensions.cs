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
    private static readonly MethodInfo GetReactiveConfigForTenantMethod =
        typeof(ConfigManager).GetMethod(nameof(ConfigManager.GetReactiveConfigForTenant))!;

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
    /// Resolves the per-tenant singleton instance of the specified feature flag class — the SAME generated
    /// class constructed with the tenant's own <see cref="IReactiveConfig{T}"/>, so the flag evaluates against
    /// that tenant's effective config (ADR-005 §7). No source-generator change. Cached per (tenant, T).
    /// </summary>
    /// <typeparam name="T">A feature flag class registered via <c>UseFeatureFlags</c>.</typeparam>
    /// <exception cref="InvalidOperationException">Flags not configured, or the tenant is not initialized.</exception>
    public static T GetFeatureFlagsForTenant<T>(this ConfigManager manager, string tenantId) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var setup = manager.FlagsSetup
            ?? throw new InvalidOperationException(
                "UseFeatureFlags has not been configured. Call .UseFeatureFlags() in ConfigManager.Create().");
        EnsureTenantInitialized(manager, tenantId);

        return (T)setup.TenantInstanceCache.GetOrAdd((tenantId, typeof(T)), _ => CreateInstanceForTenant<T>(manager, tenantId));
    }

    /// <summary>
    /// Resolves the per-tenant singleton instance of the specified entitlement class — constructed with the
    /// tenant's own <see cref="IReactiveConfig{T}"/> (ADR-005 §7). No source-generator change. Cached per (tenant, T).
    /// </summary>
    /// <typeparam name="T">An entitlement class registered via <c>UseEntitlements</c>.</typeparam>
    /// <exception cref="InvalidOperationException">Entitlements not configured, or the tenant is not initialized.</exception>
    public static T GetEntitlementsForTenant<T>(this ConfigManager manager, string tenantId) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var setup = manager.EntitlementsSetup
            ?? throw new InvalidOperationException(
                "UseEntitlements has not been configured. Call .UseEntitlements() in ConfigManager.Create().");
        EnsureTenantInitialized(manager, tenantId);

        return (T)setup.TenantInstanceCache.GetOrAdd((tenantId, typeof(T)), _ => CreateInstanceForTenant<T>(manager, tenantId));
    }

    /// <summary>
    /// Constructs a flag or entitlement class instance, resolving its <see cref="IReactiveConfig{T}"/>
    /// dependencies from the global ConfigManager. Parameterless constructors are also supported.
    /// </summary>
    private static T CreateInstance<T>(ConfigManager manager)
        => Construct<T>(p => ResolveReactiveParameter(p, configType =>
            GetReactiveConfigMethod.MakeGenericMethod(configType).Invoke(manager, null)!));

    private static void EnsureTenantInitialized(ConfigManager manager, string tenantId)
    {
        if (!manager.IsTenantInitialized(tenantId))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' is not initialized. Call InitializeTenantAsync/EnsureTenantInitializedAsync first.");
        }
    }

    /// <summary>Tenant variant of <see cref="CreateInstance{T}"/> — resolves the tenant's IReactiveConfig&lt;T&gt;.</summary>
    private static T CreateInstanceForTenant<T>(ConfigManager manager, string tenantId)
        => Construct<T>(p => ResolveReactiveParameter(p, configType =>
            GetReactiveConfigForTenantMethod.MakeGenericMethod(configType).Invoke(manager, [tenantId])!));

    private static T Construct<T>(Func<ParameterInfo, object> resolveParameter)
    {
        var type = typeof(T);

        // Prefer the constructor with the fewest parameters to handle common cases
        // where a parameterless ctor exists alongside injected ones.
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No public constructor found on '{type.Name}'.");

        var args = ctor.GetParameters().Select(resolveParameter).ToArray();
        return (T)ctor.Invoke(args);
    }

    private static object ResolveReactiveParameter(ParameterInfo param, Func<Type, object> resolveReactive)
    {
        var paramType = param.ParameterType;
        if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == ReactiveConfigDef)
        {
            var configType = paramType.GetGenericArguments()[0];
            return resolveReactive(configType);
        }

        throw new InvalidOperationException(
            $"Constructor parameter '{param.Name}' of type '{paramType.Name}' on '{param.Member.DeclaringType?.Name}' cannot be resolved. " +
            $"FeatureFlag and entitlement constructors may only depend on IReactiveConfig<T>.");
    }
}
