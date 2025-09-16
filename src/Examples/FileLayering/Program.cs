// Migrated from root Examples/FileLayering.cs

using Cocoar.Configuration;
using Cocoar.Configuration.DI;
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
        services.AddCocoarConfiguration([
            // Updated for new selection API
            Rule.From.File("base.json").Select("App").For<AppConfig>().Optional(),
            Rule.From.File("production.json").Select("App").For<AppConfig>().Optional(),
            Rule.From.File("local.json").Select("App").For<AppConfig>().Optional()
        ]);
        var serviceProvider = services.BuildServiceProvider();
        var config = serviceProvider.GetService<AppConfig>();
        Console.WriteLine($"App: {config?.ApplicationName} Version: {config?.Version} LogLevel: {config?.LogLevel}");
    }
}
