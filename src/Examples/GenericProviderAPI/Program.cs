using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddCocoarConfiguration([
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json"))
                .Select("App")
                .For<AppSettings>()
                .Required()
        ]);

        var serviceProvider = services.BuildServiceProvider();

        var config = serviceProvider.GetRequiredService<AppSettings>();

        Console.WriteLine($"App: {config.ApplicationName} FeatureA: {config.EnableFeatureA} Retries: {config.MaxRetries}");
    }
}
