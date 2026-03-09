using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace Examples.StaticProviderExample;

public sealed class CoreDefaults
{
    public string Feature { get; set; } = "A";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
}

public sealed class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public bool EnableRetries { get; set; } = true;
}

public sealed class Wrapper
{
    public CoreDefaults? Inner { get; set; }
}

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== StaticJsonProvider Demo: JSON Strings + Factory Functions ===\n");

        var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            // 1. JSON string approach - great for configuration templates, testing, defaults
            rule.For<CoreDefaults>().FromStaticJson("""
                                                    {
                                                        "Feature": "JsonBasedFeature",
                                                        "Enabled": true,
                                                        "Priority": 5
                                                    }
                                                    """),

            // 2. Direct JSON string for database configuration
            rule.For<DatabaseSettings>().FromStaticJson("""
                                                        {
                                                            "ConnectionString": "Server=localhost;Database=MyApp;Integrated Security=true",
                                                            "TimeoutSeconds": 45,
                                                            "EnableRetries": true
                                                        }
                                                        """),

            // 3. Factory approach - dynamic composition using previously resolved configs
            rule.For<Wrapper>().FromStatic(cm => {
                var coreConfig = cm.GetConfig<CoreDefaults>()!;
                return new Wrapper {
                    Inner = new CoreDefaults {
                        Feature = $"Enhanced_{coreConfig.Feature}",
                        Enabled = coreConfig.Enabled,
                        Priority = coreConfig.Priority + 10
                    }
                };
            })
        ]));

        // Retrieve and display configurations
        var coreDefaults = manager.GetConfig<CoreDefaults>()!;
        var dbSettings = manager.GetConfig<DatabaseSettings>()!;
        var wrapper = manager.GetConfig<Wrapper>()!;

        Console.WriteLine("📋 Core Configuration (from JSON string):");
        Console.WriteLine($"   Feature: {coreDefaults.Feature}");
        Console.WriteLine($"   Enabled: {coreDefaults.Enabled}");
        Console.WriteLine($"   Priority: {coreDefaults.Priority}");
        Console.WriteLine();

        Console.WriteLine("🗄️  Database Configuration (from JSON string):");
        Console.WriteLine($"   Connection: {dbSettings.ConnectionString}");
        Console.WriteLine($"   Timeout: {dbSettings.TimeoutSeconds}s");
        Console.WriteLine($"   Retries: {dbSettings.EnableRetries}");
        Console.WriteLine();

        Console.WriteLine("🔧 Wrapper Configuration (from factory using dependency):");
        Console.WriteLine($"   Enhanced Feature: {wrapper.Inner?.Feature}");
        Console.WriteLine($"   Enabled: {wrapper.Inner?.Enabled}");
        Console.WriteLine($"   Enhanced Priority: {wrapper.Inner?.Priority}");
        Console.WriteLine();

        Console.WriteLine("✅ Demonstrates:");
        Console.WriteLine("   • JSON string support for StaticJsonProvider");
        Console.WriteLine("   • Factory functions for dynamic composition"); 
        Console.WriteLine("   • Layered configuration with dependency injection");
        Console.WriteLine("   • Each rule gets isolated provider instances (no sharing)");
    }
}
