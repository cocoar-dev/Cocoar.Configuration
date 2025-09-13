/*
 * HTTP POLLING EXAMPLE  
 * 
 * Shows how to fetch configuration from HTTP endpoints with automatic polling.
 * The HTTP provider only triggers recomputes when the response payload actually changes.
 * 
 * Requires: Cocoar.Configuration.HttpPolling package
 */

using Cocoar.Configuration;
using Cocoar.Configuration.Extensions;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
// Note: This would be using Cocoar.Configuration.HttpPolling.Fluent; in real usage
using Microsoft.Extensions.DependencyInjection;

// Configuration types
public class RemoteFeatureFlags
{
    public bool EnableNewDashboard { get; set; }
    public bool EnableBetaFeatures { get; set; }
    public string[] AllowedRegions { get; set; } = Array.Empty<string>();
}

public class ApiConfiguration
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 30;
}

// Service setup with HTTP polling (simulated for this example)
var services = new ServiceCollection();

services.AddCocoarConfiguration(
    // Local base configuration
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Api"))
        .For<ApiConfiguration>()
        .Required(),

    // HTTP polling for feature flags
    // Real usage: Rule.From.Http(_ => new HttpPollingRuleOptions(...))
    Rule.From.Static<RemoteFeatureFlags>(_ => new RemoteFeatureFlags
    {
        EnableNewDashboard = true,
        EnableBetaFeatures = false,
        AllowedRegions = new[] { "us-east-1", "eu-west-1" }
    })
    .For<RemoteFeatureFlags>()
    .Optional()
);

var serviceProvider = services.BuildServiceProvider();
var configManager = serviceProvider.GetRequiredService<ConfigManager>();

var apiConfig = configManager.GetRequiredConfig<ApiConfiguration>();
var featureFlags = configManager.GetConfig<RemoteFeatureFlags>();

/*
 * REAL HTTP POLLING USAGE (requires HttpPolling package):
 * 
 * using Cocoar.Configuration.HttpPolling.Fluent;
 * 
 * Rule.From.Http(_ => new HttpPollingRuleOptions(
 *     urlPathOrAbsolute: "/api/v1/feature-flags",
 *     baseAddress: "https://config.myapp.com",
 *     pollInterval: TimeSpan.FromSeconds(30),
 *     headers: new Dictionary<string, string> 
 *     { 
 *         ["Authorization"] = "Bearer your-api-key",
 *         ["Accept"] = "application/json"
 *     }
 * ))
 * .For<RemoteFeatureFlags>()
 * .Optional()
 * 
 * EXAMPLE CONFIG - config.json:
 * {
 *   "Api": {
 *     "BaseUrl": "https://config.myapp.com",
 *     "ApiKey": "your-api-key-here",
 *     "PollIntervalSeconds": 60
 *   }
 * }
 * 
 * REMOTE ENDPOINT RESPONSE (/api/v1/feature-flags):
 * {
 *   "EnableNewDashboard": true,
 *   "EnableBetaFeatures": false,
 *   "AllowedRegions": ["us-east-1", "eu-west-1", "ap-southeast-1"]
 * }
 * 
 * KEY CONCEPTS:
 * - HTTP provider polls endpoints at specified intervals
 * - Only triggers recompute when response payload actually changes
 * - Combine with local config (base URL, API keys, poll intervals)
 * - Perfect for feature flags, remote configuration, A/B testing
 * - Handles network failures gracefully (optional rules)
 * - Custom headers for authentication, content negotiation
 */