/*
 * GENERIC PROVIDER API EXAMPLE
 * 
 * Shows how to use the generic Rule.From.Provider<TProvider, TInstanceOptions, TQueryOptions>() API
 * instead of the convenience methods like Rule.From.File(). This gives you full control over
 * any provider type, including third-party providers.
 */

using Cocoar.Configuration;
using Cocoar.Configuration.Extensions;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;

// Configuration type
public class AppSettings
{
    public string ApplicationName { get; set; } = "";
    public bool EnableFeatureA { get; set; }
    public int MaxRetries { get; set; } = 3;
}

// Service setup using generic provider API
var services = new ServiceCollection();

services.AddCocoarConfiguration(
    // Generic provider syntax - full control over provider instantiation
    Rule.From.Provider<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
        instance: _ => new FileSourceProviderOptions(
            directory: ".",
            debounceTime: TimeSpan.FromMilliseconds(100)
        ),
        query: _ => new FileSourceProviderQueryOptions(
            filename: "appsettings.json",
            sectionPath: "App"
        )
    )
    .For<AppSettings>()
    .Required()
);

var serviceProvider = services.BuildServiceProvider();
var configManager = serviceProvider.GetRequiredService<ConfigManager>();

var config = configManager.GetRequiredConfig<AppSettings>();

/*
 * EXAMPLE CONFIG - appsettings.json:
 * {
 *   "App": {
 *     "ApplicationName": "Generic Provider Demo",
 *     "EnableFeatureA": true,
 *     "MaxRetries": 5
 *   }
 * }
 * 
 * KEY CONCEPTS:
 * - Rule.From.Provider<T, TOptions, TQuery>() is the generic entry point
 * - All convenience methods (File, Environment, Http) delegate to this
 * - Useful for third-party providers or when you need full control
 * - Instance options control provider creation/pooling
 * - Query options control what data to fetch
 */