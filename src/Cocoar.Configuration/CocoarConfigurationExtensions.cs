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
            // Register concrete type with the appropriate lifetime
            RegisterServiceWithLifetime(services, type.ConcreteType, type.ServiceLifetime, type.ServiceKey, 
                _ => configManager.GetRequiredConfig(type.ConcreteType));

            // Register contract type (interface) if specified
            if (type.ContractType != null)
            {
                RegisterServiceWithLifetime(services, type.ContractType, type.ServiceLifetime, type.ServiceKey, 
                    _ => configManager.GetRequiredConfig(type.ConcreteType));
            }
        }
        return services;
    }

    private static void RegisterServiceWithLifetime(IServiceCollection services, Type serviceType, ServiceLifetime lifetime, string? serviceKey, Func<IServiceProvider, object> factory)
    {
        if (serviceKey == null)
        {
            // Register without key
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingleton(serviceType, factory);
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScoped(serviceType, factory);
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransient(serviceType, factory);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported service lifetime");
            }
        }
        else
        {
            // Register with key
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddKeyedSingleton(serviceType, serviceKey, (sp, _) => factory(sp));
                    break;
                case ServiceLifetime.Scoped:
                    services.AddKeyedScoped(serviceType, serviceKey, (sp, _) => factory(sp));
                    break;
                case ServiceLifetime.Transient:
                    services.AddKeyedTransient(serviceType, serviceKey, (sp, _) => factory(sp));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported service lifetime");
            }
        }
    }

    /// <summary>
    /// Registers via fluent rule builders (converted to rules).
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        IEnumerable<IConfigRuleBuilder> builders)
        => services.AddCocoarConfiguration(builders.SelectMany(b => b.BuildRules()));

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
