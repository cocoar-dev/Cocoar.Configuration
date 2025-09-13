/*
 * BASIC USAGE EXAMPLE
 * 
 * This shows the most common pattern for using Cocoar.Configuration:
 * - Load base configuration from JSON files  
 * - Override specific values with environment variables
 * - Use in ASP.NET Core with builder.AddCocoarConfiguration()
 * 
 * Copy this pattern into your Program.cs!
 */

using Cocoar.Configuration;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;

// 1️⃣ Define your configuration types
public interface IStartupSettings
{
    string ConnectionString { get; }
    bool EnableLogging { get; }
    int TimeoutSeconds { get; }
}

public class StartUpConfiguration : IStartupSettings
{
    public string ConnectionString { get; set; } = "";
    public bool EnableLogging { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

public class MartenStartupSettings
{
    public string DatabaseConnection { get; set; } = "";
    public bool EnableMigrations { get; set; }
    public string Schema { get; set; } = "public";
}

// 2️⃣ Configure your app - THIS IS THE MAIN PATTERN!
var builder = WebApplication.CreateBuilder(args);

builder.AddCocoarConfiguration(
    // Base configuration from files
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "StartUp"))
        .For<StartUpConfiguration>().As<IStartupSettings>().Optional(),
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Marten"))
        .For<MartenStartupSettings>().Optional(),

    // Environment variables override file settings
    Rule.From.Environment(_ => new EnvironmentVariableRuleOptions())
        .For<StartUpConfiguration>().As<IStartupSettings>(),
    Rule.From.Environment(_ => new EnvironmentVariableRuleOptions("MARTEN_"))
        .For<MartenStartupSettings>()
);

var app = builder.Build();

// 3️⃣ Use your configuration anywhere in your app
var configManager = app.Services.GetRequiredService<ConfigManager>();

var startupConfig = configManager.GetConfig<IStartupSettings>();
var martenConfig = configManager.GetConfig<MartenStartupSettings>();

// That's it! Your configuration is now available throughout your app.
// File settings provide the base values, environment variables override them.

/*
 * EXAMPLE CONFIG FILES:
 * 
 * config.json:
 * {
 *   "StartUp": {
 *     "ConnectionString": "Server=localhost;Database=MyApp;",
 *     "EnableLogging": true,
 *     "TimeoutSeconds": 30
 *   },
 *   "Marten": {
 *     "DatabaseConnection": "Server=localhost;Database=Events;", 
 *     "EnableMigrations": false,
 *     "Schema": "events"
 *   }
 * }
 * 
 * Environment variables (override file values):
 * EnableLogging=false
 * TimeoutSeconds=60
 * MARTEN_EnableMigrations=true
 * MARTEN_Schema=production
 */