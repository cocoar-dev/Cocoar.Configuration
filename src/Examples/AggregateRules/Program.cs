using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.AggregateRules;

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "";
    public int MaxPoolSize { get; set; } = 100;
    public int CommandTimeout { get; set; } = 30;
    public bool EnableRetry { get; set; }
}

public static class Program
{
    public static void Main(string[] args)
    {
        var env = "Production";
        var services = new ServiceCollection();

        services.AddCocoarConfiguration(c => c.UseConfiguration(rule =>
        [
            // FromFiles: base + environment overlay in one aggregate rule.
            // base.json is always loaded; base.Production.json merges on top.
            // If base.Production.json doesn't exist, it's silently skipped.
            rule.For<DatabaseSettings>()
                .FromFiles("base.json", $"base.{env}.json")
                .Named("DatabaseFiles")
                .Required(),

            // Environment variables override everything (separate rule, not part of aggregate).
            rule.For<DatabaseSettings>().FromEnvironment("DB_")
        ]));

        var serviceProvider = services.BuildServiceProvider();
        var config = serviceProvider.GetService<DatabaseSettings>();

        Console.WriteLine($"Connection: {config?.ConnectionString}");
        Console.WriteLine($"Pool Size:  {config?.MaxPoolSize}");
        Console.WriteLine($"Timeout:    {config?.CommandTimeout}");
        Console.WriteLine($"Retry:      {config?.EnableRetry}");
    }
}
