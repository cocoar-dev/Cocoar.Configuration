using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.AspNetCore;

public static class CocoarConfigurationAspNetCoreExtensions
{

    private static readonly ConditionalWeakTable<WebApplicationBuilder, ConfigManager> _store = new();

    /// <summary>
    /// Registers the ConfigManager and all requested config types with DI.
    /// </summary>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        IEnumerable<ConfigRule> rules)
    {

        builder.Services.ThrowIfAlreadyRegistered();

        var ruleList = rules.ToList();
        var configManager = new ConfigManager(ruleList).Initialize();
        builder.Services.AddSingleton(configManager);
        
        var types = ruleList.Select(r => r.Registration).Distinct();
        foreach (var type in types)
        {
            // Register concrete type with the appropriate lifetime
            RegisterServiceWithLifetime(builder.Services, type.ConcreteType, type.ServiceLifetime, type.ServiceKey, 
                _ => configManager.GetRequiredConfig(type.ConcreteType));

            // Register contract type (interface) if specified
            if (type.ContractType != null)
            {
                RegisterServiceWithLifetime(builder.Services, type.ContractType, type.ServiceLifetime, type.ServiceKey, 
                    _ => configManager.GetRequiredConfig(type.ConcreteType));
            }
        }
        _store.Remove(builder);
        _store.Add(builder, configManager);
        return builder;
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
    /// Overload for params usage (convenient for app code)
    /// </summary>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder services,
        params ConfigRule[] rules)
        => AddCocoarConfiguration(services, rules.AsEnumerable());

    /// <summary>
    /// Registers using fluent builders (IConfigRuleBuilder). Builders are materialized to rules internally.
    /// </summary>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        IEnumerable<IConfigRuleBuilder> builders)
        => AddCocoarConfiguration(builder, builders.SelectMany(b => b.BuildRules()));

    /// <summary>
    /// Overload for params usage with fluent builders.
    /// </summary>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        params IConfigRuleBuilder[] builders)
        => AddCocoarConfiguration(builder, builders.AsEnumerable());


    public static ConfigManager GetCocoarConfigManager(this WebApplicationBuilder builder)
        => _store.TryGetValue(builder, out var cm)
            ? cm
            : throw new InvalidOperationException("CocoarConfigManager not registered!");

    public static T? GetCocoarConfiguration<T>(this WebApplicationBuilder builder)
    {
        return builder.GetCocoarConfigManager().GetConfig<T>();
    }

    public static T GetRequiredCocoarConfiguration<T>(this WebApplicationBuilder builder)
    {
        return builder.GetCocoarConfigManager().GetRequiredConfig<T>();
    }

}
