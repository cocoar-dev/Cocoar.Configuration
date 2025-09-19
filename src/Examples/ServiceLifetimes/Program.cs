using Cocoar.Configuration;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

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

public static class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();

        services.AddCocoarConfiguration([
            Rule.From.File("config.json").Select("Database").For<DatabaseConfig>(),
            Rule.From.File("config.json").Select("Cache").For<CacheConfig>(),
            Rule.From.File("config.json").Select("PrimaryDB").For<DatabaseConfig>(),
            Rule.From.File("config.json").Select("SecondaryDB").For<DatabaseConfig>()
        ], [
            Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
        ], options =>
            options.Register
                .Add<DatabaseConfig>(ServiceLifetime.Singleton, "primary-db")
                .Add<IDatabaseConfig>(ServiceLifetime.Singleton, "primary")
                .Add<IDatabaseConfig>(ServiceLifetime.Singleton, "secondary")
        );

        var serviceProvider = services.BuildServiceProvider();

        var dbConfig = serviceProvider.GetRequiredService<IDatabaseConfig>();
        var cacheConfig = serviceProvider.GetRequiredService<CacheConfig>();
        var primaryDb = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("primary");
        var secondaryDb = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("secondary");
        var primaryConcrete = serviceProvider.GetRequiredKeyedService<DatabaseConfig>("primary-db");

        Console.WriteLine($"Primary DB: {primaryDb.ConnectionString} Secondary Timeout: {secondaryDb.TimeoutSeconds}");
    }
}
