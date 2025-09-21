using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

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

        var manager = new ConfigManager([
            // 1. JSON string approach - great for configuration templates, testing, defaults
            Rule.From.StaticJson("""
            {
                "Feature": "JsonBasedFeature",
                "Enabled": true,
                "Priority": 5
            }
            """).For<CoreDefaults>(),

            // 2. Direct JSON string for database configuration  
            Rule.From.StaticJson("""
            {
                "ConnectionString": "Server=localhost;Database=MyApp;Integrated Security=true",
                "TimeoutSeconds": 45,
                "EnableRetries": true
            }
            """).For<DatabaseSettings>(),

            // 3. Factory approach - dynamic composition using previously resolved configs
            Rule.From.Static(cm => {
                var coreConfig = cm.GetRequiredConfig<CoreDefaults>();
                return new Wrapper { 
                    Inner = new CoreDefaults {
                        Feature = $"Enhanced_{coreConfig.Feature}",
                        Enabled = coreConfig.Enabled,
                        Priority = coreConfig.Priority + 10
                    }
                };
            }).For<Wrapper>()
        ]).Initialize();

        // Retrieve and display configurations
        var coreDefaults = manager.GetRequiredConfig<CoreDefaults>();
        var dbSettings = manager.GetRequiredConfig<DatabaseSettings>();  
        var wrapper = manager.GetRequiredConfig<Wrapper>();

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
