/*
 * DYNAMIC DEPENDENCIES EXAMPLE
 * 
 * Shows how later configuration rules can read values from earlier rules
 * during recompute. This enables powerful patterns where one config controls another.
 * 
 * Real-world examples:
 * - Config file provides API endpoint, HTTP rule fetches from that endpoint
 * - Environment sets region, later rule loads region-specific settings
 * - Database config provides connection, later rule fetches feature flags
 */

using Cocoar.Configuration;
using Cocoar.Configuration.Extensions;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider.Fluent;
using Microsoft.Extensions.DependencyInjection;

// Configuration types
public class ApiSettings
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class FeatureFlags
{
    public bool EnableNewDashboard { get; set; }
    public bool EnableBetaFeatures { get; set; }
    public string Theme { get; set; } = "default";
}

public class RegionSettings
{
    public string Region { get; set; } = "";
    public string DataCenter { get; set; } = "";
}

public class RegionSpecificConfig
{
    public string DatabaseEndpoint { get; set; } = "";
    public string CdnUrl { get; set; } = "";
    public string[] AvailableLanguages { get; set; } = Array.Empty<string>();
}

// Service setup with dynamic dependencies
var services = new ServiceCollection();

services.AddCocoarConfiguration(
    // 1️⃣ Base API settings loaded first
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Api"))
        .For<ApiSettings>()
        .Required(),

    // 2️⃣ Feature flags that depend on API settings
    Rule.From.Static<FeatureFlags>(configManager =>
    {
        var apiSettings = configManager.GetRequiredConfig<ApiSettings>();
        
        // In real scenario, you'd make HTTP call to apiSettings.BaseUrl
        // For this example, we simulate based on the URL
        if (apiSettings.BaseUrl.Contains("staging"))
        {
            return new FeatureFlags
            {
                EnableNewDashboard = true,
                EnableBetaFeatures = true,
                Theme = "staging"
            };
        }
        
        return new FeatureFlags
        {
            EnableNewDashboard = false,
            EnableBetaFeatures = false,
            Theme = "production"
        };
    }).For<FeatureFlags>(),

    // 3️⃣ Region-based configuration
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Region"))
        .For<RegionSettings>()
        .Required(),

    Rule.From.Static<RegionSpecificConfig>(configManager =>
    {
        var regionSettings = configManager.GetRequiredConfig<RegionSettings>();
        
        // Load region-specific configuration
        return regionSettings.Region switch
        {
            "us-west-2" => new RegionSpecificConfig
            {
                DatabaseEndpoint = "db-oregon.example.com",
                CdnUrl = "https://cdn-us-west.example.com",
                AvailableLanguages = new[] { "en", "es" }
            },
            "eu-central-1" => new RegionSpecificConfig
            {
                DatabaseEndpoint = "db-frankfurt.example.com", 
                CdnUrl = "https://cdn-eu-central.example.com",
                AvailableLanguages = new[] { "en", "de", "fr" }
            },
            _ => new RegionSpecificConfig
            {
                DatabaseEndpoint = "db-global.example.com",
                CdnUrl = "https://cdn-global.example.com", 
                AvailableLanguages = new[] { "en" }
            }
        };
    }).For<RegionSpecificConfig>()
);

var serviceProvider = services.BuildServiceProvider();
var configManager = serviceProvider.GetRequiredService<ConfigManager>();

// Usage
var apiSettings = configManager.GetConfig<ApiSettings>();
var featureFlags = configManager.GetConfig<FeatureFlags>();
var regionConfig = configManager.GetConfig<RegionSpecificConfig>();

/*
 * EXAMPLE CONFIG - config.json:
 * {
 *   "Api": {
 *     "BaseUrl": "https://api.staging.example.com",
 *     "ApiKey": "dev-api-key-123"
 *   },
 *   "Region": {
 *     "Region": "eu-central-1",
 *     "DataCenter": "frankfurt"
 *   }
 * }
 * 
 * WHAT HAPPENS:
 * 1. API settings loaded from file
 * 2. Feature flags rule reads API settings, sees "staging" in URL → enables beta features
 * 3. Region settings loaded from file  
 * 4. Region-specific rule reads region "eu-central-1" → loads Frankfurt-specific config
 * 
 * KEY CONCEPTS:
 * - Rules process in order during recompute
 * - Later rules can read current snapshot with configManager.GetRequiredConfig<T>()
 * - Use GetConfig<T>() for optional dependencies (returns null if missing)
 * - Use GetRequiredConfig<T>() for required dependencies (throws if missing)
 * - Perfect for API-driven configuration, region-based settings, feature flags
 */