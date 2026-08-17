using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.MicrosoftAdapter;
using Cocoar.Configuration.Providers;
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

public class CollectionSettings
{
    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();
}

public class ForwardedHeadersSettings
{
    public List<string> KnownNetworks { get; set; } = [];
}

public static class Program
{
    public static void Main(string[] args)
    {
        const string environmentPrefix = "COCOAR_COLLECTION_EXAMPLE_";
        SetDefaultEnvironmentVariable(
            environmentPrefix + "ForwardedHeaders__KnownNetworks__4",
            "10.40.0.0/16");
        SetDefaultEnvironmentVariable(
            environmentPrefix + "ForwardedHeaders__KnownNetworks__0",
            "10.10.10.0/24");
        SetDefaultEnvironmentVariable(
            environmentPrefix + "ForwardedHeaders__KnownNetworks__2",
            "10.20.0.0/16");

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
            .AddEnvironmentVariables(environmentPrefix)
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

        CompareIndexedCollectionBinding(configuration, environmentPrefix);
    }

    private static void CompareIndexedCollectionBinding(
        IConfiguration configuration,
        string environmentPrefix)
    {
        var microsoftNetworks = configuration
            .GetSection("ForwardedHeaders:KnownNetworks")
            .Get<string[]>() ?? [];

        using var environmentManager = ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<CollectionSettings>().FromEnvironment(environmentPrefix)
        ]));
        var cocoarEnvironmentNetworks = environmentManager
            .GetConfig<CollectionSettings>()!
            .ForwardedHeaders
            .KnownNetworks;

        using var adapterManager = ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<CollectionSettings>().FromIConfiguration(configuration)
        ]));
        var cocoarAdapterNetworks = adapterManager
            .GetConfig<CollectionSettings>()!
            .ForwardedHeaders
            .KnownNetworks;

        if (!microsoftNetworks.SequenceEqual(cocoarEnvironmentNetworks)
            || !microsoftNetworks.SequenceEqual(cocoarAdapterNetworks))
        {
            throw new InvalidOperationException(
                "Indexed collection binding differs between Microsoft and Cocoar configuration.");
        }

        Console.WriteLine($"Microsoft binder:       {string.Join(", ", microsoftNetworks)}");
        Console.WriteLine($"Cocoar environment:     {string.Join(", ", cocoarEnvironmentNetworks)}");
        Console.WriteLine($"Cocoar Microsoft adapter: {string.Join(", ", cocoarAdapterNetworks)}");
        Console.WriteLine("Indexed collection binding matches.");
    }

    private static void SetDefaultEnvironmentVariable(string name, string value)
    {
        if (Environment.GetEnvironmentVariable(name) is null)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
