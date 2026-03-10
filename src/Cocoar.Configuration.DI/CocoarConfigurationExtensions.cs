using Cocoar.Configuration.Core;
using Cocoar.Configuration.Health;
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
        services.AddSingleton<IConfigurationHealthService>(sp => sp.GetRequiredService<ConfigManager>().GetHealthService());

        // Build registration plan and apply it
        var plan = ServiceRegistrationPlanner.CreatePlan(configManager);
        ServiceDescriptorEmitter.Emit(services, plan);

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
        services.AddCocoarConfiguration(configManager);
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
