// Migrated from root Examples/FileLayering.cs

using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;

public class AppConfig
{
    public string ApplicationName { get; set; } = "";
    public string Version { get; set; } = "";
    public bool EnableLogging { get; set; }
    public string LogLevel { get; set; } = "Information";
    public int MaxConnections { get; set; } = 100;
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

public static class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("base.json", "App"))
                .For<AppConfig>()
                .Optional(),
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("production.json", "App"))
                .For<AppConfig>()
                .Optional(),
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("local.json", "App"))
                .For<AppConfig>()
                .Optional()
        );
        var serviceProvider = services.BuildServiceProvider();
        var configManager = serviceProvider.GetRequiredService<ConfigManager>();
        var config = configManager.GetConfig<AppConfig>();
        Console.WriteLine($"App: {config?.ApplicationName} Version: {config?.Version} LogLevel: {config?.LogLevel}");
    }
}