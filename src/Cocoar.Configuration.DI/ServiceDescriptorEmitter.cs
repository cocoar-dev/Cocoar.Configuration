using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Flags.Internal;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.SecretTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Applies a service registration plan to an IServiceCollection.
/// Separates DI manipulation from interpretation logic.
/// </summary>
internal static class ServiceDescriptorEmitter
{
    /// <summary>
    /// Applies the registration plan to the service collection.
    /// Also emits feature flags and entitlements registrations when the corresponding
    /// capabilities are present on the ConfigManager's capability scope.
    /// </summary>
    public static void Emit(
        IServiceCollection services,
        Dictionary<Type, ServiceRegistrationInfo> plan,
        ConfigManager configManager)
    {
        // Explicit ordering for deterministic emission (belt-and-suspenders with planner sort)
        foreach (var (serviceType, value) in plan.OrderBy(kvp => kvp.Key.FullName))
        {
            EmitConfigService(services, serviceType, value);
            EmitReactiveService(services, serviceType);
        }

        EmitFlagsServices(services, configManager);
        EmitEntitlementsServices(services, configManager);
        EmitProviderContributedServices(services, configManager);
        EmitSecretsKeyProviderServices(services, configManager);
    }

    /// <summary>
    /// Discovers provider options that implement <see cref="IProviderServiceRegistration"/> and applies the
    /// extra service registrations they contribute (e.g. WritableStore's <c>IWritableStore&lt;T&gt;</c> /
    /// <c>IWritableStoreOverlay&lt;T&gt;</c>). Registrations may be eager singleton instances or resolve-time
    /// factories. Collisions on the same service type are last-rule-wins; emission is ordered by service type
    /// full name for determinism.
    /// </summary>
    private static void EmitProviderContributedServices(IServiceCollection services, ConfigManager configManager)
    {
        var registrations = new Dictionary<Type, ProviderServiceRegistration>();

        foreach (var rule in configManager.Rules)
        {
            IProviderConfiguration options;
            try
            {
                options = rule.ResolveProviderOptions(configManager);
            }
            catch
            {
                // A rule whose options can't be resolved at setup time contributes no services here;
                // the recompute pipeline surfaces any real failure.
                continue;
            }

            if (options is not IProviderServiceRegistration provider)
            {
                continue;
            }

            foreach (var registration in provider.GetServiceRegistrations(rule.ConcreteType))
            {
                registrations[registration.ServiceType] = registration; // last-rule-wins
            }
        }

        foreach (var (serviceType, registration) in registrations.OrderBy(kvp => kvp.Key.FullName))
        {
            if (registration.Instance is not null)
            {
                services.AddSingleton(serviceType, registration.Instance);
            }
            else
            {
                services.AddSingleton(serviceType, registration.Factory!);
            }
        }
    }

    /// <summary>
    /// Registers the public <see cref="ISecretEncryptionKeyProvider"/> when a publishable encryption
    /// key is configured — single-kid and folder/multi-tenant secrets each compose an
    /// <see cref="ISecretEncryptionKeyInfoProvider"/>. The provider resolves the capability lazily per
    /// call so certificate rotation is reflected. Not registered when no secrets are configured.
    /// </summary>
    private static void EmitSecretsKeyProviderServices(IServiceCollection services, ConfigManager configManager)
    {
        var keyInfoProviders = configManager.CapabilityScope.Owner.GetComposition()
            ?.GetAll<ISecretEncryptionKeyInfoProvider>();
        if (keyInfoProviders is null || keyInfoProviders.Count == 0)
            return;

        services.AddSingleton<ISecretEncryptionKeyProvider>(
            sp => new SecretEncryptionKeyProvider(sp.GetRequiredService<ConfigManager>().CapabilityScope));
    }

    private static void EmitFlagsServices(IServiceCollection services, ConfigManager configManager)
    {
        if (configManager.FlagsSetup is not { } capability)
            return;

        services.AddSingleton<IFeatureFlagsDescriptors>(capability.Descriptors);
        foreach (var r in capability.Registrations)
            services.Add(new ServiceDescriptor(r.Descriptor.Type, r.Descriptor.Type, ServiceLifetime.Singleton));

        // Register all resolver types with lifetimes from Capabilities
        EmitResolverServices(services,
            capability.Registrations.SelectMany(r => r.Resolvers),
            capability.GlobalResolvers,
            capability.ResolverRegistrations);

        // IFeatureFlagEvaluator is Scoped so it holds the current request's IServiceProvider —
        // resolvers may have scoped dependencies (e.g. DbContext) that must be resolved from the request scope.
        var evaluationEntries = capability.EvaluationEntries;
        services.AddScoped<IFeatureFlagEvaluator>(sp => new FeatureFlagEvaluator(evaluationEntries, sp));
    }

    private static void EmitEntitlementsServices(IServiceCollection services, ConfigManager configManager)
    {
        if (configManager.EntitlementsSetup is not { } capability)
            return;

        services.AddSingleton<IEntitlementsDescriptors>(capability.Descriptors);
        foreach (var r in capability.Registrations)
            services.Add(new ServiceDescriptor(r.Descriptor.Type, r.Descriptor.Type, ServiceLifetime.Singleton));

        // Register all resolver types with lifetimes from Capabilities
        EmitResolverServices(services,
            capability.Registrations.SelectMany(r => r.Resolvers),
            capability.GlobalResolvers,
            capability.ResolverRegistrations);

        // IEntitlementEvaluator is Scoped so it holds the current request's IServiceProvider —
        // resolvers may have scoped dependencies (e.g. DbContext) that must be resolved from the request scope.
        var evaluationEntries = capability.EvaluationEntries;
        services.AddScoped<IEntitlementEvaluator>(sp => new EntitlementEvaluator(evaluationEntries, sp));
    }

    /// <summary>
    /// Registers resolver types in DI. Reads lifetime from the <see cref="ResolverRegistration"/>
    /// Capabilities system. Defaults to Scoped when no capability is set.
    /// </summary>
    private static void EmitResolverServices(
        IServiceCollection services,
        IEnumerable<ContextResolverRegistration> classResolvers,
        IReadOnlyList<ContextResolverRegistration> globalResolvers,
        object[]? resolverRegistrations)
    {
        // Build a lookup from resolver type → lifetime using the DI-layer ResolverRegistration capabilities
        var lifetimeOverrides = new Dictionary<Type, ServiceLifetime>();
        if (resolverRegistrations is not null)
        {
            foreach (var reg in resolverRegistrations.OfType<ResolverRegistration>())
            {
                var lifetime = reg.GetLifetime();
                foreach (var r in reg.Registrations)
                    lifetimeOverrides[r.ResolverType] = lifetime;
            }
        }

        // Deduplicate resolver types across class-level and global registrations
        var resolverTypes = classResolvers
            .Concat(globalResolvers)
            .Select(r => r.ResolverType)
            .Distinct();

        foreach (var resolverType in resolverTypes)
        {
            var lifetime = lifetimeOverrides.GetValueOrDefault(resolverType, ServiceLifetime.Scoped);
            services.Add(new ServiceDescriptor(resolverType, resolverType, lifetime));
        }
    }

    private static void EmitConfigService(
        IServiceCollection services,
        Type serviceType,
        ServiceRegistrationInfo info)
    {
        // Default registration (scoped) if not disabled and not overwritten
        if (info is { DisableDefault: false, OverwriteDefault: false })
        {
            services.Add(new ServiceDescriptor(
                serviceType,
                sp => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!,
                ServiceLifetime.Scoped));
        }

        // Custom lifetime registrations (keyed or unkeyed)
        foreach (var (serviceKey, serviceLifetime) in info.ServiceLifetimes)
        {
            if (serviceKey is "")
            {
                services.Add(new ServiceDescriptor(
                    serviceType,
                    sp => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!,
                    serviceLifetime));
            }
            else
            {
                services.Add(new ServiceDescriptor(
                    serviceType,
                    serviceKey,
                    (sp, _) => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!,
                    serviceLifetime));
            }
        }
    }

    private static void EmitReactiveService(IServiceCollection services, Type serviceType)
    {
        var reactiveType = typeof(IReactiveConfig<>).MakeGenericType(serviceType);
        services.AddSingleton(reactiveType, sp =>
        {
            var mgr = sp.GetRequiredService<ConfigManager>();
            var method = mgr.GetType().GetMethod("GetReactiveConfig")!.MakeGenericMethod(serviceType);
            return method.Invoke(mgr, null)!;
        });
    }
}
