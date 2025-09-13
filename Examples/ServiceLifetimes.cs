/*
 * SERVICE LIFETIMES EXAMPLE
 * 
 * Shows how to control how configuration types are registered in DI.
 * By default, configurations are registered as Singleton, but you can
 * control lifetimes for both concrete types and interfaces.
 */

using Cocoar.Configuration;
using Cocoar.Configuration.Extensions;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Microsoft.Extensions.DependencyInjection;

// Configuration types
public interface IDatabaseConfig
{
    string ConnectionString { get; }
    int TimeoutSeconds { get; }
}

public class DatabaseConfig : IDatabaseConfig
{
    public string ConnectionString { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

public class CacheConfig
{
    public string RedisConnection { get; set; } = "";
    public bool EnableDistributedCache { get; set; }
}

// Service collection setup
var services = new ServiceCollection();

services.AddCocoarConfiguration(
    // Default: Singleton registration for interface
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Database"))
        .For<DatabaseConfig>()
        .As<IDatabaseConfig>(),  // Registered as Singleton

    // Explicit lifetime for concrete type
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "Cache"))
        .For<CacheConfig>(ServiceLifetime.Scoped),  // Scoped lifetime

    // Multiple configurations with keyed services
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "PrimaryDB"))
        .For<DatabaseConfig>(ServiceLifetime.Singleton, "primary-db")
        .As<IDatabaseConfig>(ServiceLifetime.Singleton, "primary"),
        
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config.json", "SecondaryDB"))  
        .For<DatabaseConfig>()
        .As<IDatabaseConfig>(ServiceLifetime.Singleton, "secondary")
);

var serviceProvider = services.BuildServiceProvider();

// Usage - regular services
var dbConfig = serviceProvider.GetRequiredService<IDatabaseConfig>();
var cacheConfig = serviceProvider.GetRequiredService<CacheConfig>();

// Usage - keyed services for multiple configurations
var primaryDb = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("primary");
var secondaryDb = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("secondary");
var primaryConcrete = serviceProvider.GetRequiredKeyedService<DatabaseConfig>("primary-db");

/*
 * EXAMPLE CONFIG - config.json:
 * {
 *   "Database": {
 *     "ConnectionString": "Server=localhost;Database=Main;",
 *     "TimeoutSeconds": 30
 *   },
 *   "Cache": {
 *     "RedisConnection": "localhost:6379", 
 *     "EnableDistributedCache": true
 *   },
 *   "PrimaryDB": {
 *     "ConnectionString": "Server=primary;Database=Main;",
 *     "TimeoutSeconds": 30
 *   },
 *   "SecondaryDB": {
 *     "ConnectionString": "Server=secondary;Database=Backup;",
 *     "TimeoutSeconds": 60
 *   }
 * }
 * 
 * KEY CONCEPTS:
 * - Default lifetime is Singleton
 * - Use ServiceLifetime parameter to change lifetime
 * - Keyed services allow multiple configs of same type
 * - You can register both concrete type AND interface with different lifetimes
 */