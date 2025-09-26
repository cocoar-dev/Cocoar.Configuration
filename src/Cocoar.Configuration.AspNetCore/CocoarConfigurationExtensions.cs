using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.AspNetCore;

public static class CocoarConfigurationAspNetCoreExtensions
{
    private static readonly ConditionalWeakTable<WebApplicationBuilder, ConfigManager> _store = new();

    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        IEnumerable<ConfigRule> rules,
        IEnumerable<BindingSpec>? bindings = null,
        Action<ServiceRegistrationOptions>? configureOptions = null,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {
        var ruleList = rules.ToList();
        var bindingList = bindings?.ToList() ?? new List<BindingSpec>();

        var configManager = new ConfigManager(ruleList, bindingList, logger, debounceMilliseconds: debounceMilliseconds);
        configManager.Initialize();

        builder.Services.AddCocoarConfiguration(configManager, options =>
        {
            options.DefaultRegistrationLifetime(ServiceLifetime.Scoped);
            configureOptions?.Invoke(options);
        });

        _store.Remove(builder);
        _store.Add(builder, configManager);
        return builder;
    }


    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        IEnumerable<ConfigRule> rules,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {
        return builder.AddCocoarConfiguration(rules, null, null, logger, debounceMilliseconds);
    }


    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        params ConfigRule[] rules)
        => AddCocoarConfiguration(builder, rules.AsEnumerable(), null, null);


    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        IEnumerable<IConfigRuleBuilder> builders)
        => AddCocoarConfiguration(builder, builders.Select(b => b.Build()), null, null);

    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        params IConfigRuleBuilder[] builders)
        => AddCocoarConfiguration(builder, builders.AsEnumerable());


    public static ConfigManager GetCocoarConfigManager(this WebApplicationBuilder builder)
        => _store.TryGetValue(builder, out var cm)
            ? cm
            : throw new InvalidOperationException("CocoarConfigManager not registered!");

}
