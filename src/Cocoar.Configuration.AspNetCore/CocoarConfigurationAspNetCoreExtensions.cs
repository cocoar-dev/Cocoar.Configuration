using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Fluent;
using Cocoar.Capabilities;

namespace Cocoar.Configuration.AspNetCore;

public static class CocoarConfigurationAspNetCoreExtensions
{
    // Use Cocoar.Capabilities infrastructure instead of ConditionalWeakTable
    private static readonly CapabilityScope _capabilityScope = new();

    /// <summary>
    /// Adds Cocoar configuration to the WebApplicationBuilder using a function-based rule API.
    /// </summary>
    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        Func<RulesBuilder, ConfigRule[]> rule,
        Func<SetupBuilder, SetupDefinition[]>? configure = null,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {
        var rulesBuilder = new RulesBuilder();
        var ruleList = rule(rulesBuilder);
        
        var configManager = new ConfigManager(ruleList, configure, logger, debounceMilliseconds: debounceMilliseconds);
        configManager.Initialize();

        builder.Services.AddCocoarConfiguration(configManager);

        // Attach ConfigManager as a capability to the WebApplicationBuilder
        _capabilityScope.Compose(builder).Add(configManager).Build();
        
        return builder;
    }

    public static WebApplicationBuilder AddCocoarConfiguration(
        this WebApplicationBuilder builder,
        ConfigManager configManager)
    {
        builder.Services.AddCocoarConfiguration(configManager);

        // Attach ConfigManager as a capability to the WebApplicationBuilder
        _capabilityScope.Compose(builder).Add(configManager).Build();
        
        return builder;
    }

    public static ConfigManager GetCocoarConfigManager(this WebApplicationBuilder builder)
        => _capabilityScope.Compositions.GetRequired(builder).GetRequiredFirst<ConfigManager>();

}
