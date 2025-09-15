// Migrated from root Examples/DynamicDependencies.cs

using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Microsoft.Extensions.DependencyInjection;

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

public static class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json")).Select("Api")
                .For<ApiSettings>()
                .Required(),
            Rule.From.Static<FeatureFlags>(configManager =>
            {
                var apiSettings = configManager.GetRequiredConfig<ApiSettings>();
                if (apiSettings.BaseUrl.Contains("staging"))
                {
                    return new FeatureFlags { EnableNewDashboard = true, EnableBetaFeatures = true, Theme = "staging" };        
                }
                return new FeatureFlags { EnableNewDashboard = false, EnableBetaFeatures = false, Theme = "production" };
            }).For<FeatureFlags>(),
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json")).Select("Region")
                .For<RegionSettings>()
                .Required(),
            Rule.From.Static<RegionSpecificConfig>(configManager =>
            {
                var regionSettings = configManager.GetRequiredConfig<RegionSettings>();
                return regionSettings.Region switch
                {
                    "us-west-2" => new RegionSpecificConfig { DatabaseEndpoint = "db-oregon.example.com", CdnUrl = "https://cdn-us-west.example.com", AvailableLanguages = new[] { "en", "es" } },
                    "eu-central-1" => new RegionSpecificConfig { DatabaseEndpoint = "db-frankfurt.example.com", CdnUrl = "https://cdn-eu-central.example.com", AvailableLanguages = new[] { "en", "de", "fr" } },
                    _ => new RegionSpecificConfig { DatabaseEndpoint = "db-global.example.com", CdnUrl = "https://cdn-global.example.com", AvailableLanguages = new[] { "en" } }
                };
            }).For<RegionSpecificConfig>()
        );
        var serviceProvider = services.BuildServiceProvider();
        var apiSettings = serviceProvider.GetRequiredService<ApiSettings>();
        var featureFlags = serviceProvider.GetService<FeatureFlags>();
        var regionConfig = serviceProvider.GetService<RegionSpecificConfig>();
        Console.WriteLine($"API: {apiSettings.BaseUrl} Flags theme: {featureFlags?.Theme} Region DB: {regionConfig?.DatabaseEndpoint}");
    }
}