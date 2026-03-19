using Cocoar.Configuration.DI;
using Cocoar.Configuration.MicrosoftAdapter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.MicrosoftAdapterExample;

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "";
    public bool EnableRetries { get; set; }
    public int CommandTimeout { get; set; } = 30;
}

public class AppSettings
{
    public string ApplicationName { get; set; } = "";
    public string Version { get; set; } = "";
}

public static class Program
{
    public static void Main(string[] args)
    {
        // Build an IConfiguration from any Microsoft configuration sources
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
                ["Database:EnableRetries"] = "true",
                ["Database:CommandTimeout"] = "45",
                ["App:ApplicationName"] = "Microsoft Adapter Demo",
                ["App:Version"] = "2.1.0"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [

            // Simple: pass IConfiguration directly, use .Select() to scope to a section
            rule.For<DatabaseSettings>()
                .FromIConfiguration(configuration).Select("Database")
                .Required(),

            rule.For<AppSettings>()
                .FromIConfiguration(configuration).Select("App")
                .Required()

        ]));

        var serviceProvider = services.BuildServiceProvider();

        var dbSettings = serviceProvider.GetRequiredService<DatabaseSettings>();
        var appSettings = serviceProvider.GetRequiredService<AppSettings>();

        Console.WriteLine($"DB Timeout: {dbSettings.CommandTimeout} App: {appSettings.ApplicationName} v{appSettings.Version}");
    }
}
