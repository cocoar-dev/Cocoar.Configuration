using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
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

        services.AddCocoarConfiguration(rule => [
            rule.For<AppConfig>().FromFile("base.json").Select("App"),
            rule.For<AppConfig>().FromFile("production.json").Select("App"),
            rule.For<AppConfig>().FromFile("local.json").Select("App")
        ]);

        var serviceProvider = services.BuildServiceProvider();
        var config = serviceProvider.GetService<AppConfig>();

        Console.WriteLine($"App: {config?.ApplicationName} Version: {config?.Version} LogLevel: {config?.LogLevel}");
    }
}
