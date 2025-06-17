using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Extensions.Extensions;

public static class CocoarConfigurationExtensions
{
    /// <summary>
    /// Registers the ConfigManager and all requested config types with DI.
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        IEnumerable<ConfigRule> rules)
    {
        var ruleList = rules.ToList();
        services.AddSingleton(sp => new ConfigManager(sp, ruleList).Initialize());
        var types = ruleList.Select(r => r.ConfigContract).Distinct();
        foreach (var type in types)
        {
            var lastRule = ruleList.Last(r => r.ConfigContract == type);
            var lifetime = lastRule.Lifetime ?? ConfigLifetime.Scoped;
            Func<IServiceProvider, object> factory =
                sp =>
                {
                    return sp.GetRequiredService<ConfigManager>().GetConfig(type.ConfigType)!;
                };
            lifetime = ConfigLifetime.Singleton;
            switch (lifetime)
            {
                case ConfigLifetime.Singleton:
                    if (type.ImplementationType != null)
                    {
                        services.AddSingleton(type.ImplementationType, factory);
                    }
                    services.AddSingleton(type.ConfigType, factory);
                    break;
                case ConfigLifetime.Transient:
                    if (type.ImplementationType != null)
                    {
                        services.AddTransient(type.ImplementationType, factory);
                    }
                    services.AddTransient(type.ConfigType, factory);
                    break;
                default:
                    if (type.ImplementationType != null)
                    {
                        services.AddScoped(type.ImplementationType, factory);
                    }
                    services.AddScoped(type.ConfigType, factory);
                    break;
            }
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
}
