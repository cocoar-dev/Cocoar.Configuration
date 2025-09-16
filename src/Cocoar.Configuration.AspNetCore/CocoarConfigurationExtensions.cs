using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.DI;

namespace Cocoar.Configuration.AspNetCore;

public static class CocoarConfigurationAspNetCoreExtensions
{
    private static readonly ConditionalWeakTable<WebApplicationBuilder, ConfigManager> _store = new();

    /// <summary>
    /// Registers the ConfigManager with ASP.NET Core dependency injection.
    /// Uses the same API as ConfigManager for consistency.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <param name="rules">Configuration rules defining where configurations come from</param>
    /// <param name="bindings">Optional binding specifications defining interface mappings</param>
    /// <param name="configureOptions">Optional action to configure service registration options</param>
    /// <param name="logger">Optional logger for ConfigManager</param>
    /// <param name="debounceMilliseconds">Debounce time for configuration change notifications</param>
    /// <returns>The WebApplicationBuilder for method chaining</returns>
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

        // Create ConfigManager (same as core API)
        var configManager = new ConfigManager(ruleList, bindingList, logger, debounceMilliseconds: debounceMilliseconds);
        configManager.Initialize();

        // Use DI project to register with ASP.NET Core's default Scoped lifetime
        builder.Services.AddCocoarConfiguration(configManager, options =>
        {
            // ASP.NET Core defaults to Scoped lifetime which is perfect for web applications
            options.DefaultRegistrationLifetime(ServiceLifetime.Scoped);
            // Apply user configuration
            configureOptions?.Invoke(options);
        });

        // Store ConfigManager per WebApplicationBuilder for build-time access
        _store.Remove(builder);
        _store.Add(builder, configManager);
        return builder;
    }

    /// <summary>
    /// Registers the ConfigManager with ASP.NET Core dependency injection (simple overload).
    /// This overload is convenient when you only need rules without bindings.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <param name="rules">Configuration rules defining where configurations come from</param>
    /// <param name="logger">Optional logger for ConfigManager</param>
    /// <param name="debounceMilliseconds">Debounce time for configuration change notifications</param>
    /// <returns>The WebApplicationBuilder for method chaining</returns>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        IEnumerable<ConfigRule> rules,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {
        return builder.AddCocoarConfiguration(rules, null, null, logger, debounceMilliseconds);
    }

    /// <summary>
    /// Overload for params usage (convenient for app code)
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <param name="rules">Configuration rules as params</param>
    /// <returns>The WebApplicationBuilder for method chaining</returns>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        params ConfigRule[] rules)
        => AddCocoarConfiguration(builder, rules.AsEnumerable(), null, null, null, 300);

    /// <summary>
    /// Registers using fluent builders (IConfigRuleBuilder). Builders are materialized to rules internally.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <param name="builders">Configuration rule builders</param>
    /// <returns>The WebApplicationBuilder for method chaining</returns>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        IEnumerable<IConfigRuleBuilder> builders)
        => AddCocoarConfiguration(builder, builders.SelectMany(b => b.BuildRules()), null, null, null, 300);

    /// <summary>
    /// Overload for params usage with fluent builders.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <param name="builders">Configuration rule builders as params</param>
    /// <returns>The WebApplicationBuilder for method chaining</returns>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        params IConfigRuleBuilder[] builders)
        => AddCocoarConfiguration(builder, builders.AsEnumerable());

    /// <summary>
    /// Gets the registered ConfigManager from the WebApplicationBuilder.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <returns>The ConfigManager instance</returns>
    /// <exception cref="InvalidOperationException">Thrown if ConfigManager not registered</exception>
    public static ConfigManager GetCocoarConfigManager(this WebApplicationBuilder builder)
        => _store.TryGetValue(builder, out var cm)
            ? cm
            : throw new InvalidOperationException("CocoarConfigManager not registered!");

    /// <summary>
    /// Gets configuration of type T from the registered ConfigManager.
    /// </summary>
    /// <typeparam name="T">The configuration type</typeparam>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <returns>The configuration instance or null if not found</returns>
    public static T? GetCocoarConfiguration<T>(this WebApplicationBuilder builder)
    {
        return builder.GetCocoarConfigManager().GetConfig<T>();
    }

    /// <summary>
    /// Gets required configuration of type T from the registered ConfigManager.
    /// </summary>
    /// <typeparam name="T">The configuration type</typeparam>
    /// <param name="builder">The WebApplicationBuilder</param>
    /// <returns>The configuration instance</returns>
    /// <exception cref="InvalidOperationException">Thrown if configuration not found</exception>
    public static T GetRequiredCocoarConfiguration<T>(this WebApplicationBuilder builder)
    {
        return builder.GetCocoarConfigManager().GetRequiredConfig<T>();
    }
}
