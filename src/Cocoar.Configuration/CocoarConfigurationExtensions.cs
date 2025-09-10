using Cocoar.Configuration.Fluent;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration;

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
        services.AddSingleton(configManager);
        var types = ruleList.Select(r => r.Registration).Distinct();
        foreach (var type in types)
        {
            if (type.ContractType != null)
            {
                services.AddSingleton(type.ContractType, _ => configManager.GetRequiredConfig(type.ConcreteType));
            }
            services.AddSingleton(type.ConcreteType, _ => configManager.GetRequiredConfig(type.ConcreteType));
        }
        return services;
    }

    /// <summary>
    /// Registers via fluent rule builders (converted to rules).
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        IEnumerable<IConfigRuleBuilder> builders)
        => services.AddCocoarConfiguration(builders.Select(b => b.Build()));

    /// <summary>
    /// Overload for params usage (convenient for app code)
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        params ConfigRule[] rules)
        => services.AddCocoarConfiguration(rules.AsEnumerable());

    /// <summary>
    /// Overload for params builders usage.
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        params IConfigRuleBuilder[] builders)
        => services.AddCocoarConfiguration(builders.AsEnumerable());


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
