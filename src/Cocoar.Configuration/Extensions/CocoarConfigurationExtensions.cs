using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Extensions;

public static class CocoarConfigurationExtensions
{
    /// <summary>
    /// Registers the ConfigManager and all requested config types with DI.
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        IEnumerable<ConfigRule> rules)
    {
        services.ThrowIfAlreadyRegistered();

        var ruleList = rules.ToList();
        var configManager = new ConfigManager(ruleList).Initialize();
        services.AddSingleton<ConfigManager>(configManager);
        var types = ruleList.Select(r => r.ConfigContract).Distinct();
        foreach (var type in types)
        {
            if (type.ImplementationType != null)
            {
                services.AddSingleton(type.ImplementationType, sp => configManager.GetRequiredConfig(type.ConfigType));
            }
            services.AddSingleton(type.ConfigType, sp => configManager.GetRequiredConfig(type.ConfigType));
        }
        return services;
    }

    /// <summary>
    /// Overload for params usage (convenient for app code)
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        params ConfigRule[] rules)
        => AddCocoarConfiguration(services, rules.AsEnumerable());


    public static void ThrowIfAlreadyRegistered(this IServiceCollection services)
    {
        if (services.Any(sd => sd.ServiceType == typeof(ConfigManager)))
        {
            throw new InvalidOperationException(
                "Cocoar: A ConfigManager is already registered! Do not register multiple times or mix Core and ASP.NET extensions."
            );
        }
    }
}
