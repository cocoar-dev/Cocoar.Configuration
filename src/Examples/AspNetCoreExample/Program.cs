using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace Examples.AspNetCoreExample;

public interface IAppSettings
{
    string ApplicationName { get; }
    string Version { get; }
    bool IsProduction { get; }
}

public class AppSettings : IAppSettings
{
    public string ApplicationName { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public bool IsProduction { get; set; }
}

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeout { get; set; } = 30;
    public bool EnableRetryOnFailure { get; set; } = true;
}

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddCocoarConfiguration(rule => [
            rule.For<AppSettings>().FromFile("config.json").Select("App"),
            rule.For<AppSettings>().FromEnvironment("APP_"),
            rule.For<DatabaseSettings>().FromFile("config.json").Select("Database"),
            rule.For<DatabaseSettings>().FromEnvironment("DB_")
        ], setup => [
            setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),
        ]);

        var app = builder.Build();

        app.MapGet("/config", (IAppSettings appSettings, DatabaseSettings? dbSettings) => new
        {
            Application = new { appSettings.ApplicationName, appSettings.Version, appSettings.IsProduction },
            Database = new { HasConnectionString = !string.IsNullOrEmpty(dbSettings?.ConnectionString), dbSettings?.CommandTimeout, dbSettings?.EnableRetryOnFailure }
        });

        app.MapGet("/health", (IAppSettings appSettings) => new
        {
            Status = "Healthy",
            Application = appSettings.ApplicationName,
            Version = appSettings.Version,
            Environment = appSettings.IsProduction ? "Production" : "Development"
        });

        app.MapGet("/manager", (ConfigManager manager) =>
        {
            var appSettings = manager.GetRequiredConfig<IAppSettings>();
            var dbSettings = manager.GetConfig<DatabaseSettings>();
            return new
            {
                RetrievedVia = "ConfigManager",
                Application = new { appSettings.ApplicationName, appSettings.Version, appSettings.IsProduction },
                Database = new { HasConnectionString = !string.IsNullOrEmpty(dbSettings?.ConnectionString), dbSettings?.CommandTimeout, dbSettings?.EnableRetryOnFailure }
            };
        });

        app.Run();
    }
}
