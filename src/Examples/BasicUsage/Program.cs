using Cocoar.Configuration;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

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

        builder.AddCocoarConfiguration(rule => [
            rule.File("config.json").Select("StartUp").For<StartUpConfiguration>(),
            rule.File("config.json").Select("Marten").For<MartenStartupSettings>(),
            rule.Environment().For<StartUpConfiguration>(),
            rule.Environment("MARTEN_").For<MartenStartupSettings>()
        ], setup => [
            setup.ConcreteType<StartUpConfiguration>().ExposeAs<IStartupSettings>()
        ]);

        var app = builder.Build();

        var startupConfig = app.Services.GetService<IStartupSettings>();
        var martenConfig = app.Services.GetService<MartenStartupSettings>();

        Console.WriteLine($"Startup: {startupConfig?.ConnectionString} Logging: {startupConfig?.EnableLogging} Timeout: {startupConfig?.TimeoutSeconds}");
        Console.WriteLine($"Marten: {martenConfig?.DatabaseConnection} Migrations: {martenConfig?.EnableMigrations} Schema: {martenConfig?.Schema}");
    }
}
