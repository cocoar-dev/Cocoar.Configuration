using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

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

public static class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();

        services.AddCocoarConfiguration([

            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json")).Select("Api")
                .For<ApiConfiguration>()
                .Required(),

            Rule.From.Static<RemoteFeatureFlags>(_ => new RemoteFeatureFlags
            {
                EnableNewDashboard = true,
                EnableBetaFeatures = false,
                AllowedRegions = new[] { "us-east-1", "eu-west-1" }
            })
            .For<RemoteFeatureFlags>()

        ]);

        var serviceProvider = services.BuildServiceProvider();

        var apiConfig = serviceProvider.GetRequiredService<ApiConfiguration>();
        var featureFlags = serviceProvider.GetService<RemoteFeatureFlags>();

        Console.WriteLine($"API Base: {apiConfig.BaseUrl} FeatureFlags Beta: {featureFlags?.EnableBetaFeatures}");
    }
}
