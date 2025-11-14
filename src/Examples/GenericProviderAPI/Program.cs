using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.GenericProviderAPI;

public class AppSettings
{
    public string ApplicationName { get; set; } = "";
    public bool EnableFeatureA { get; set; }
    public int MaxRetries { get; set; } = 3;
}

public static class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();

        services.AddCocoarConfiguration(rule => [
            rule.For<AppSettings>().FromFile(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json"))
                .Select("App")
                .Required()
        ]);

        var serviceProvider = services.BuildServiceProvider();

        var config = serviceProvider.GetRequiredService<AppSettings>();

        Console.WriteLine($"App: {config.ApplicationName} FeatureA: {config.EnableFeatureA} Retries: {config.MaxRetries}");
    }
}
