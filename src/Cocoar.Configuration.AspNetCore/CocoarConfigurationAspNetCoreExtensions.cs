using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;

namespace Cocoar.Configuration.AspNetCore;

public static class CocoarConfigurationAspNetCoreExtensions
{
    private static readonly ConditionalWeakTable<WebApplicationBuilder, ConfigManager> _registrations = new();

    /// <summary>
    /// Adds Cocoar configuration to the WebApplicationBuilder using the builder API.
    /// Provides access to the full ConfigManagerBuilder for configuring rules, setup, secrets, logging, etc.
    /// Test configuration overrides are automatically applied via ConfigManager when CocoarTestConfiguration is active.
    /// </summary>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        Action<ConfigManagerBuilder> configure)
    {
        var configManager = ConfigManager.Create(configure);

        builder.Services.AddCocoarConfiguration(configManager);
        _registrations.AddOrUpdate(builder, configManager);

        return builder;
    }

    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        ConfigManager configManager)
    {
        builder.Services.AddCocoarConfiguration(configManager);
        _registrations.AddOrUpdate(builder, configManager);

        return builder;
    }

    public static ConfigManager GetCocoarConfigManager(this WebApplicationBuilder builder)
    {
        if (!_registrations.TryGetValue(builder, out var configManager))
        {
            throw new InvalidOperationException(
                "No ConfigManager has been registered for this WebApplicationBuilder. " +
                "Call AddCocoarConfiguration() before GetCocoarConfigManager().");
        }
        return configManager;
    }
}
