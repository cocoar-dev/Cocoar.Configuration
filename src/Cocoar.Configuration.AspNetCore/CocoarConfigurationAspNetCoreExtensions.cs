using Microsoft.AspNetCore.Builder;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Capabilities;

namespace Cocoar.Configuration.AspNetCore;

public static class CocoarConfigurationAspNetCoreExtensions
{
    // Use Cocoar.Capabilities infrastructure instead of ConditionalWeakTable
    private static readonly CapabilityScope _capabilityScope = new();

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
        _capabilityScope.Compose(builder).Add(configManager).Build();

        return builder;
    }

    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        ConfigManager configManager)
    {
        builder.Services.AddCocoarConfiguration(configManager);
        _capabilityScope.Compose(builder).Add(configManager).Build();

        return builder;
    }

    public static ConfigManager GetCocoarConfigManager(this WebApplicationBuilder builder)
        => _capabilityScope.Compositions.GetRequired(builder).GetRequiredFirst<ConfigManager>();
}
