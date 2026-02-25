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
        var services = new ServiceCollection();

        services.AddCocoarConfiguration(c => c.WithConfiguration(rule => [

            rule.For<DatabaseSettings>().FromMicrosoftSource(_ => new(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
                        ["Database:EnableRetries"] = "true",
                        ["Database:CommandTimeout"] = "45",
                        ["App:ApplicationName"] = "Microsoft Adapter Demo",
                        ["App:Version"] = "2.1.0"
                    }).Sources[0],
                    configurationPrefix: "Database"
                ))
                .Required(),

            rule.For<AppSettings>().FromMicrosoftSource(_ => new(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["App:ApplicationName"] = "Microsoft Adapter Demo",
                        ["App:Version"] = "2.1.0"
                    }).Sources[0],
                    configurationPrefix: "App"
                ))
                .Required()

        ]));

        var serviceProvider = services.BuildServiceProvider();

        var dbSettings = serviceProvider.GetRequiredService<DatabaseSettings>();
        var appSettings = serviceProvider.GetRequiredService<AppSettings>();

        Console.WriteLine($"DB Timeout: {dbSettings.CommandTimeout} App: {appSettings.ApplicationName} v{appSettings.Version}");
    }
}
