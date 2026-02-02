using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// Adds Cocoar configuration to the service collection using a function-based rule API.
    /// Test configuration overrides are automatically applied via ConfigManager when CocoarTestConfiguration is active.
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        Func<RulesBuilder, ConfigRule[]> rule,
        Func<SetupBuilder, SetupDefinition[]>? setup = null,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {
        services.ThrowIfAlreadyRegistered();

        var configManager = new ConfigManager(rule, setup, logger, debounceMilliseconds: debounceMilliseconds);
        configManager.Initialize();

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
