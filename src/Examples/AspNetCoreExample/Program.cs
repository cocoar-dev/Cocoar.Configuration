using Cocoar.Configuration;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

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

        builder.AddCocoarConfiguration([
            Rule.From.File("config.json").Select("App").For<AppSettings>(),
            Rule.From.Environment("APP_").For<AppSettings>(),
            Rule.From.File("config.json").Select("Database").For<DatabaseSettings>(),
            Rule.From.Environment("DB_").For<DatabaseSettings>()
        ], [
            Bind.Type<AppSettings>().To<IAppSettings>()
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
