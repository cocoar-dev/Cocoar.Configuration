using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Cocoar.Configuration.Extensions;
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
        builder.Services.AddSingleton<ConfigManager>(configManager);
        var types = ruleList.Select(r => r.ConfigContract).Distinct();
        foreach (var type in types)
        {
            if (type.ImplementationType != null)
            {
                builder.Services.AddSingleton(type.ImplementationType, sp => configManager.GetRequiredConfig(type.ConfigType));
            }
            builder.Services.AddSingleton(type.ConfigType, sp => configManager.GetRequiredConfig(type.ConfigType));
        }
        _store.Remove(builder);
        _store.Add(builder, configManager);
        return builder;
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
        => AddCocoarConfiguration(builder, builders.Select(b => b.Build()));

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
