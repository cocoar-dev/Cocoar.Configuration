using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace CommandLineExample;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== CommandLine Provider Test ===\n");
        Console.WriteLine($"Arguments: {string.Join(" ", args)}\n");

        // Test 1: Default (--) prefix
        TestDefault(args);

        // Test 2: Multiple prefixes
        TestMultiplePrefixes(args);

        // Test 3: Semantic prefixes
        TestSemanticPrefixes(args);

        // Test 4: Prefix filtering
        TestPrefixFiltering(args);

        // Test 5: Nested configuration
        TestNestedConfiguration(args);
    }

    static void TestDefault(string[] args)
    {
        Console.WriteLine("--- Test 1: Default (--) prefix ---");
        try
        {
            using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
                rule.For<AppConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })
            ]));

            var config = manager.GetConfig<AppConfig>();
            Console.WriteLine($"Host: {config?.Host ?? "null"}");
            Console.WriteLine($"Port: {config?.Port ?? 0}");
            Console.WriteLine($"Verbose: {config?.Verbose ?? false}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        Console.WriteLine();
    }

    static void TestMultiplePrefixes(string[] args)
    {
        Console.WriteLine("--- Test 2: Multiple prefixes (--, -, /) ---");
        try
        {
            using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
                rule.For<AppConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    SwitchPrefixes = ["--", "-", "/"]
                })
            ]));

            var config = manager.GetConfig<AppConfig>();
            Console.WriteLine($"Host: {config?.Host ?? "null"}");
            Console.WriteLine($"Port: {config?.Port ?? 0}");
            Console.WriteLine($"Verbose: {config?.Verbose ?? false}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        Console.WriteLine();
    }

    static void TestSemanticPrefixes(string[] args)
    {
        Console.WriteLine("--- Test 3: Semantic prefixes (@, #, %) ---");
        try
        {
            using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
                rule.For<TargetConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    SwitchPrefixes = ["@"]
                }),
                rule.For<IssueConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    SwitchPrefixes = ["#"]
                }),
                rule.For<EnvConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    SwitchPrefixes = ["%"]
                })
            ]));

            var target = manager.GetConfig<TargetConfig>();
            var issue = manager.GetConfig<IssueConfig>();
            var env = manager.GetConfig<EnvConfig>();

            Console.WriteLine($"Target Host: {target?.Host ?? "null"}");
            Console.WriteLine($"Issue ID: {issue?.Id ?? 0}");
            Console.WriteLine($"Environment: {env?.Name ?? "null"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        Console.WriteLine();
    }

    static void TestPrefixFiltering(string[] args)
    {
        Console.WriteLine("--- Test 4: Prefix filtering (app_, db_) ---");
        try
        {
            using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
                rule.For<AppConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    Prefix = "app_"
                }),
                rule.For<DatabaseConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    Prefix = "db_"
                })
            ]));

            var app = manager.GetConfig<AppConfig>();
            var db = manager.GetConfig<DatabaseConfig>();

            Console.WriteLine($"App Host: {app?.Host ?? "null"}");
            Console.WriteLine($"App Port: {app?.Port ?? 0}");
            Console.WriteLine($"DB ConnectionString: {db?.ConnectionString ?? "null"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        Console.WriteLine();
    }

    static void TestNestedConfiguration(string[] args)
    {
        Console.WriteLine("--- Test 5: Nested configuration (: and __) ---");
        try
        {
            using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
                rule.For<ServerConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })
            ]));

            var config = manager.GetConfig<ServerConfig>();
            Console.WriteLine($"Database Host: {config?.Database?.Host ?? "null"}");
            Console.WriteLine($"Database Port: {config?.Database?.Port ?? 0}");
            Console.WriteLine($"Cache Host: {config?.Cache?.Host ?? "null"}");
            Console.WriteLine($"Cache Ttl: {config?.Cache?.Ttl ?? 0}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        Console.WriteLine();
    }
}

// Config classes
public class AppConfig
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public bool Verbose { get; set; }
}

public class TargetConfig
{
    public string? Host { get; set; }
}

public class IssueConfig
{
    public int Id { get; set; }
}

public class EnvConfig
{
    public string? Name { get; set; }
}

public class DatabaseConfig
{
    public string? ConnectionString { get; set; }
}

public class ServerConfig
{
    public DatabaseSettings? Database { get; set; }
    public CacheSettings? Cache { get; set; }
}

public class DatabaseSettings
{
    public string? Host { get; set; }
    public int Port { get; set; }
}

public class CacheSettings
{
    public string? Host { get; set; }
    public int Ttl { get; set; }
}

