/*
 * FILE LAYERING EXAMPLE
 * 
 * Shows how to merge configuration from multiple JSON files.
 * Later files override earlier files on a key-by-key basis (last-write-wins).
 * 
 * Common patterns:
 * - base.json + environment-specific.json
 * - shared.json + application-specific.json  
 * - config.json + local-overrides.json
 */

using Cocoar.Configuration;
using Cocoar.Configuration.Extensions;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Microsoft.Extensions.DependencyInjection;

// Configuration type
public class AppConfig
{
    public string ApplicationName { get; set; } = "";
    public string Version { get; set; } = "";
    public bool EnableLogging { get; set; }
    public string LogLevel { get; set; } = "Information";
    public int MaxConnections { get; set; } = 100;
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

// Service setup with file layering
var services = new ServiceCollection();

services.AddCocoarConfiguration(
    // 1️⃣ Base configuration (lowest priority)
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("base.json", "App"))
        .For<AppConfig>()
        .Optional(),

    // 2️⃣ Environment-specific overrides (medium priority)  
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("production.json", "App"))
        .For<AppConfig>()
        .Optional(),

    // 3️⃣ Local overrides (highest priority)
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("local.json", "App"))
        .For<AppConfig>()
        .Optional()
);

var serviceProvider = services.BuildServiceProvider();
var configManager = serviceProvider.GetRequiredService<ConfigManager>();

var config = configManager.GetConfig<AppConfig>();

/*
 * EXAMPLE FILES:
 * 
 * base.json:
 * {
 *   "App": {
 *     "ApplicationName": "My Application",
 *     "Version": "1.0.0",
 *     "EnableLogging": true,
 *     "LogLevel": "Information", 
 *     "MaxConnections": 100,
 *     "AllowedOrigins": ["http://localhost:3000"]
 *   }
 * }
 * 
 * production.json:
 * {
 *   "App": {
 *     "EnableLogging": true,
 *     "LogLevel": "Warning",
 *     "MaxConnections": 500,
 *     "AllowedOrigins": ["https://myapp.com", "https://www.myapp.com"]
 *   }
 * }
 * 
 * local.json:
 * {
 *   "App": {
 *     "LogLevel": "Debug",
 *     "MaxConnections": 10
 *   }
 * }
 * 
 * RESULT (merged configuration):
 * {
 *   "ApplicationName": "My Application",     // from base.json
 *   "Version": "1.0.0",                     // from base.json  
 *   "EnableLogging": true,                  // from production.json (overrode base.json)
 *   "LogLevel": "Debug",                    // from local.json (overrode production.json)
 *   "MaxConnections": 10,                   // from local.json (overrode production.json) 
 *   "AllowedOrigins": ["https://myapp.com", "https://www.myapp.com"]  // from production.json
 * }
 * 
 * KEY CONCEPTS:
 * - Files are processed in rule order
 * - Later rules override earlier rules key-by-key
 * - Missing files are ignored (Optional())
 * - Arrays are replaced completely (not merged)
 * - Perfect for environment-specific configuration
 */