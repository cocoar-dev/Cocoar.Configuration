// Migrated from root Examples/GenericProviderAPI.cs

using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;

public class AppSettings
{
    public string ApplicationName { get; set; } = "";
    public bool EnableFeatureA { get; set; }
    public int MaxRetries { get; set; } = 3;
}

public static class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(
            Rule.FromProvider<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
                instanceOptions: _ => new FileSourceProviderOptions(directory: ".", debounceTime: TimeSpan.FromMilliseconds(100)),
                queryOptions: _ => new FileSourceProviderQueryOptions(Filename: "appsettings.json", ConfigurationPath: "App")
            )
            .For<AppSettings>()
            .Required()
        );
        var serviceProvider = services.BuildServiceProvider();
        var config = serviceProvider.GetRequiredService<AppSettings>();
        Console.WriteLine($"App: {config.ApplicationName} FeatureA: {config.EnableFeatureA} Retries: {config.MaxRetries}");
    }
}