using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Rules;

namespace SimplifiedCoreExample;

// Configuration POCOs (no attributes, no interface exposure here - pure data)
public class AppConfig
{
    public string ApplicationName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Environment { get; set; } = "";
    public string LogLevel { get; set; } = "Information";
}

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeout { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
}

public class FeatureConfig
{
    public bool EnableNewDashboard { get; set; }
    public bool EnableExperimentalFeatures { get; set; }
    public int MaxConcurrentUsers { get; set; } = 50;
    public bool CacheEnabled { get; set; } = true;
}

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== Cocoar.Configuration Simplified Core Example ===");
        Console.WriteLine("(No DI, no interface exposure - direct concrete type retrieval only)");
        Console.WriteLine();

        // 1. Build rules using the function-based API
        var manager = new ConfigManager(rule => [
            rule.File(_ => FileSourceRuleOptions.FromFilePath("config/app.json"))
                .For<AppConfig>(),
                
            rule.File(_ => FileSourceRuleOptions.FromFilePath("config/database.json"))
                .For<DatabaseConfig>(),
                
            rule.File(_ => FileSourceRuleOptions.FromFilePath("config/features.json"))
                .For<FeatureConfig>()
        ]).Initialize();
        
        Console.WriteLine("📋 Configuration Manager initialized");
        Console.WriteLine();

        // 3. Retrieve configurations by concrete type only
        try
        {
            var appConfig = manager.GetConfig<AppConfig>();
            var dbConfig = manager.GetConfig<DatabaseConfig>();
            var featureConfig = manager.GetConfig<FeatureConfig>();

            Console.WriteLine("✅ Successfully loaded all configurations:");
            Console.WriteLine();
            
            Console.WriteLine("🏗️  App Configuration:");
            Console.WriteLine("   Name: {0}", appConfig.ApplicationName);
            Console.WriteLine("   Version: {0}", appConfig.Version);
            Console.WriteLine("   Environment: {0}", appConfig.Environment);
            Console.WriteLine("   LogLevel: {0}", appConfig.LogLevel);
            Console.WriteLine();
            
            Console.WriteLine("🗄️  Database Configuration:");
            Console.WriteLine("   ConnectionString: {0}", MaskConnectionString(dbConfig.ConnectionString));
            Console.WriteLine("   CommandTimeout: {0}s", dbConfig.CommandTimeout);
            Console.WriteLine("   MaxRetries: {0}", dbConfig.MaxRetries);
            Console.WriteLine();
            
            Console.WriteLine("🎛️  Feature Configuration:");
            Console.WriteLine("   EnableNewDashboard: {0}", featureConfig.EnableNewDashboard);
            Console.WriteLine("   EnableExperimentalFeatures: {0}", featureConfig.EnableExperimentalFeatures);
            Console.WriteLine("   MaxConcurrentUsers: {0}", featureConfig.MaxConcurrentUsers);
            Console.WriteLine("   CacheEnabled: {0}", featureConfig.CacheEnabled);
            Console.WriteLine();

            // 4. Demonstrate rule layering by creating a scenario with overrides
            Console.WriteLine("🔄 Demonstrating rule layering (later rules override earlier ones):");
            
            var layeredManager = new ConfigManager(rule => [
                // Base configuration
                rule.File(_ => FileSourceRuleOptions.FromFilePath("config/app.json"))
                    .For<AppConfig>(),
                    
                // Override via static JSON (simulating environment-specific override)
                rule.Static(_ => new {
                    Environment = "Production",
                    LogLevel = "Warning"
                }).For<AppConfig>()
            ]).Initialize();
            
            var overriddenApp = layeredManager.GetConfig<AppConfig>();
            
            Console.WriteLine("   Original Environment: Development → Overridden: {0}", overriddenApp.Environment);
            Console.WriteLine("   Original LogLevel: Information → Overridden: {0}", overriddenApp.LogLevel);
            Console.WriteLine("   ApplicationName (unchanged): {0}", overriddenApp.ApplicationName);
            Console.WriteLine();

            // 5. Show configuration access patterns
            Console.WriteLine("📖 Key API Patterns in Simplified Core:");
            Console.WriteLine("   ✓ rule.File(...).For<ConcreteType>()");
            Console.WriteLine("   ✓ rule.Static(...).For<ConcreteType>()");
            Console.WriteLine("   ✓ new ConfigManager(rule => [...]).Initialize()");
            Console.WriteLine("   ✓ manager.GetConfig<ConcreteType>()");
            Console.WriteLine("   ✗ No .As<Interface>() (removed from core)");
            Console.WriteLine("   ✗ No service lifetimes (moved to DI package)");
            Console.WriteLine("   ✗ No AddCocoarConfiguration() (moved to DI package)");
            Console.WriteLine();
            
            Console.WriteLine("🎯 This example demonstrates the core library after DI separation.");
            Console.WriteLine("   For DI integration, use Cocoar.Configuration.DI package.");
            Console.WriteLine("   For interface exposure, future TypeExposureRegistry will be added.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Error loading configuration: {0}", ex.Message);
            Console.WriteLine("   Stack trace: {0}", ex.StackTrace);
        }
        
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
    
    private static string MaskConnectionString(string connectionString)
    {
        // Simple masking for demo purposes
        if (connectionString.Length > 20)
            return connectionString.Substring(0, 20) + "***";
        return connectionString;
    }
}
