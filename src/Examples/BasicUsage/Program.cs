using Cocoar.Configuration;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;

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

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddCocoarConfiguration(
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "StartUp"))
                .For<StartUpConfiguration>().As<IStartupSettings>().Optional(),
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Marten"))
                .For<MartenStartupSettings>().Optional(),
            Rule.From.Environment(_ => new EnvironmentVariableRuleOptions())
                .For<StartUpConfiguration>().As<IStartupSettings>(),
            Rule.From.Environment(_ => new EnvironmentVariableRuleOptions("MARTEN_"))
                .For<MartenStartupSettings>()
        );
        var app = builder.Build();
        var configManager = app.Services.GetRequiredService<ConfigManager>();
        var startupConfig = configManager.GetConfig<IStartupSettings>();
        var martenConfig = configManager.GetConfig<MartenStartupSettings>();
        Console.WriteLine($"Startup: {startupConfig?.ConnectionString} Logging: {startupConfig?.EnableLogging} Timeout: {startupConfig?.TimeoutSeconds}");
        Console.WriteLine($"Marten: {martenConfig?.DatabaseConnection} Migrations: {martenConfig?.EnableMigrations} Schema: {martenConfig?.Schema}");
    }
}