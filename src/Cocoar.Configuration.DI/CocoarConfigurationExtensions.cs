using Cocoar.Configuration.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

public static class CocoarConfigurationExtensions
{
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        ConfigManager configManager)
    {
        services.ThrowIfAlreadyRegistered();

        // Register core services
        services.AddSingleton(configManager);
        services.AddSingleton<IConfigurationAccessor>(sp => sp.GetRequiredService<ConfigManager>());

        // Build registration plan and apply it
        var plan = ServiceRegistrationPlanner.CreatePlan(configManager);
        ServiceDescriptorEmitter.Emit(services, plan, configManager);

        // ADR-006: if UseServiceBackedConfiguration added Layer-2 rules, register the holder + activation hosted
        // service so they come alive on host start. No-op otherwise (zero impact for Layer-1-only apps). Wired
        // HERE — the single point every entry path funnels through (DI Action overload, AspNetCore, manual).
        ServiceBackedConfigurationCoordinator.WireActivation(services, configManager);

        return services;
    }

    /// <summary>
    /// Adds Cocoar configuration to the service collection using the builder API.
    /// Provides access to the full ConfigManagerBuilder for configuring rules, setup, secrets, logging, etc.
    /// Test configuration overrides are automatically applied via ConfigManager when CocoarTestConfiguration is active.
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        Action<ConfigManagerBuilder> configure)
    {
        services.ThrowIfAlreadyRegistered();

        var configManager = ConfigManager.Create(configure);
        services.AddCocoarConfiguration(configManager); // WireActivation runs inside the instance overload
        return services;
    }

    private static void ThrowIfAlreadyRegistered(this IServiceCollection services)
    {
        if (services.Any(s => s.ServiceType == typeof(ConfigManager)))
        {
            throw new InvalidOperationException("Cocoar Configuration has already been registered. AddCocoarConfiguration should only be called once.");
        }
    }
}
