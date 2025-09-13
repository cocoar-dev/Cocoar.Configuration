/*
 * ASP.NET CORE EXAMPLE
 * 
 * Complete working ASP.NET Core application that demonstrates:
 * - Loading configuration from multiple sources
 * - Exposing configuration via API endpoints
 * - Using configuration in controllers/services
 * 
 * Run this example:
 * 1. Create config.json in the same folder
 * 2. dotnet run 
 * 3. Visit http://localhost:5000/config to see your configuration
 */

using Cocoar.Configuration;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;

// Configuration types
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

// ASP.NET Core setup
var builder = WebApplication.CreateBuilder(args);

// Add Cocoar Configuration
builder.AddCocoarConfiguration(
    // Application settings from config file + environment
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "App"))
        .For<AppSettings>().As<IAppSettings>().Optional(),
    Rule.From.Environment(_ => new EnvironmentVariableRuleOptions("APP_"))
        .For<AppSettings>().As<IAppSettings>(),

    // Database settings
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Database"))
        .For<DatabaseSettings>().Optional(),
    Rule.From.Environment(_ => new EnvironmentVariableRuleOptions("DB_"))
        .For<DatabaseSettings>()
);

var app = builder.Build();

// API endpoint to view configuration
app.MapGet("/config", (ConfigManager configManager) =>
{
    var appSettings = configManager.GetConfig<IAppSettings>();
    var dbSettings = configManager.GetConfig<DatabaseSettings>();

    return new
    {
        Application = new
        {
            appSettings?.ApplicationName,
            appSettings?.Version,
            appSettings?.IsProduction
        },
        Database = new
        {
            HasConnectionString = !string.IsNullOrEmpty(dbSettings?.ConnectionString),
            dbSettings?.CommandTimeout,
            dbSettings?.EnableRetryOnFailure
        }
    };
});

// Another endpoint showing configuration in a service
app.MapGet("/health", (IAppSettings appSettings) =>
{
    return new
    {
        Status = "Healthy",
        Application = appSettings.ApplicationName,
        Version = appSettings.Version,
        Environment = appSettings.IsProduction ? "Production" : "Development"
    };
});

app.Run();

/*
 * EXAMPLE CONFIG FILE - config.json:
 * {
 *   "App": {
 *     "ApplicationName": "My Cool API",
 *     "Version": "2.1.0",
 *     "IsProduction": false
 *   },
 *   "Database": {
 *     "ConnectionString": "Server=localhost;Database=MyApp;Trusted_Connection=true;",
 *     "CommandTimeout": 45,
 *     "EnableRetryOnFailure": true
 *   }
 * }
 * 
 * ENVIRONMENT OVERRIDES:
 * APP_ApplicationName="Production API"
 * APP_IsProduction=true
 * DB_ConnectionString="Server=prod-db;Database=MyApp;User Id=app;Password=***;"
 * 
 * USAGE:
 * 1. Create config.json with above content
 * 2. dotnet run
 * 3. Open browser:
 *    - http://localhost:5000/config (shows merged configuration)
 *    - http://localhost:5000/health (shows app info)
 * 4. Try setting environment variables and restart to see overrides
 */