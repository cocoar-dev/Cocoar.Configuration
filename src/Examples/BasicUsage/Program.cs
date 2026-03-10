using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Providers;

namespace Examples.BasicUsage;

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

        builder.AddCocoarConfiguration(c => c.UseConfiguration(rule => [
            rule.For<StartUpConfiguration>().FromFile("config.json").Select("StartUp"),
            rule.For<MartenStartupSettings>().FromFile("config.json").Select("Marten"),
            rule.For<StartUpConfiguration>().FromEnvironment(),
            rule.For<MartenStartupSettings>().FromEnvironment("MARTEN_")
        ], setup => [
            setup.ConcreteType<StartUpConfiguration>().ExposeAs<IStartupSettings>()
        ]));

        var app = builder.Build();

        var startupConfig = app.Services.GetService<IStartupSettings>();
        var martenConfig = app.Services.GetService<MartenStartupSettings>();

        Console.WriteLine($"Startup: {startupConfig?.ConnectionString} Logging: {startupConfig?.EnableLogging} Timeout: {startupConfig?.TimeoutSeconds}");
        Console.WriteLine($"Marten: {martenConfig?.DatabaseConnection} Migrations: {martenConfig?.EnableMigrations} Schema: {martenConfig?.Schema}");
    }
}
