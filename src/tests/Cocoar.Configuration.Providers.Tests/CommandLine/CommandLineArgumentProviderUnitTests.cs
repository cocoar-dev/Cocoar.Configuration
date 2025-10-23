using Xunit;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Providers.Tests.CommandLine;

public class CommandLineArgumentProviderUnitTests
{
    private static readonly RulesBuilder Builder = new();
    
    private sealed class SimpleConfig { public string? Host { get; set; } public int Port { get; set; } }
    private sealed class DatabaseConfig { public string? Host { get; set; } public int Port { get; set; } public string? Name { get; set; } }
    private sealed class NestedConfig { public DatabaseConfig? Database { get; set; } }
    private sealed class FlagsConfig { public bool Debug { get; set; } public bool Verbose { get; set; } }
    private sealed class MixedConfig { public string? Host { get; set; } public int Port { get; set; } public bool Debug { get; set; } }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void EmptyArgs_ReturnsEmptyConfiguration()
    {
        var args = Array.Empty<string>();
        
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Null(cfg!.Host);
        Assert.Equal(0, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void EqualsFormat_ParsesCorrectly()
    {
        var args = new[] { "--host=localhost", "--port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void SpaceFormat_ParsesCorrectly()
    {
        var args = new[] { "--host", "localhost", "--port", "8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void MixedFormats_ParsesCorrectly()
    {
        var args = new[] { "--host=localhost", "--port", "8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void BooleanFlags_ParseAsTrue()
    {
        var args = new[] { "--debug", "--verbose" };
        using var manager = new ConfigManager(rule => [rule.For<FlagsConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<FlagsConfig>();
        Assert.NotNull(cfg);
        Assert.True(cfg!.Debug);
        Assert.True(cfg.Verbose);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void NestedConfiguration_WithColon_ParsesCorrectly()
    {
        var args = new[] { "--database:host=localhost", "--database:port=5432", "--database:name=mydb" };
        using var manager = new ConfigManager(rule => [rule.For<NestedConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<NestedConfig>();
        Assert.NotNull(cfg);
        Assert.NotNull(cfg!.Database);
        Assert.Equal("localhost", cfg.Database!.Host);
        Assert.Equal(5432, cfg.Database.Port);
        Assert.Equal("mydb", cfg.Database.Name);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void NestedConfiguration_WithDoubleUnderscore_ParsesCorrectly()
    {
        var args = new[] { "--database__host=localhost", "--database__port=5432" };
        using var manager = new ConfigManager(rule => [rule.For<NestedConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<NestedConfig>();
        Assert.NotNull(cfg);
        Assert.NotNull(cfg!.Database);
        Assert.Equal("localhost", cfg.Database!.Host);
        Assert.Equal(5432, cfg.Database.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void SingleDashPrefix_ParsesCorrectly()
    {
        var args = new[] { "-host=localhost", "-port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions
        {
            Args = args,
            SwitchPrefixes = ["-"]
        })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void NonSwitchArguments_AreIgnored()
    {
        var args = new[] { "somecommand", "--host=localhost", "anotherarg", "--port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void CaseInsensitive_ParsesCorrectly()
    {
        var args = new[] { "--HOST=localhost", "--Port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void FluentAPI_WithArgs_Works()
    {
        var args = new[] { "--host=localhost", "--port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void EmptyValueAfterEquals_ParsesAsEmptyString()
    {
        var args = new[] { "--host=", "--port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void ValueWithSpaces_InEqualsFormat_ParsesCorrectly()
    {
        var args = new[] { "--host=my server name", "--port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("my server name", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void LastValueWins_WhenDuplicateKeys()
    {
        var args = new[] { "--host=server1", "--host=server2", "--port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("server2", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void RealWorldScenario_DockerStyle()
    {
        // Simulating: docker run myapp --host=localhost --port=8080 --debug
        var args = new[] { "--host=localhost", "--port=8080", "--debug" };
        using var manager = new ConfigManager(rule => [rule.For<MixedConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<MixedConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
        Assert.True(cfg.Debug);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void Prefix_FiltersAndStripsPrefix()
    {
        var args = new[] { "--app_host=localhost", "--app_port=8080", "--db_host=dbserver" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args, Prefix = "app_" })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void Prefix_WithMultipleTypes_MapsCorrectly()
    {
        var args = new[] { "--app_host=localhost", "--app_port=8080", "--db_host=dbserver", "--db_port=5432" };
        using var manager = new ConfigManager(rule => [
            rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args, Prefix = "app_" }),
            rule.For<DatabaseConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args, Prefix = "db_" })
        ]);
        manager.Initialize();

        var appCfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(appCfg);
        Assert.Equal("localhost", appCfg!.Host);
        Assert.Equal(8080, appCfg.Port);

        var dbCfg = manager.GetConfig<DatabaseConfig>();
        Assert.NotNull(dbCfg);
        Assert.Equal("dbserver", dbCfg!.Host);
        Assert.Equal(5432, dbCfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void Prefix_WithNestedKeys_WorksCorrectly()
    {
        var args = new[] { "--app_database:host=localhost", "--app_database:port=5432" };
        using var manager = new ConfigManager(rule => [rule.For<NestedConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args, Prefix = "app_" })]);
        manager.Initialize();

        var cfg = manager.GetConfig<NestedConfig>();
        Assert.NotNull(cfg);
        Assert.NotNull(cfg!.Database);
        Assert.Equal("localhost", cfg.Database!.Host);
        Assert.Equal(5432, cfg.Database.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void Prefix_CaseInsensitive()
    {
        var args = new[] { "--APP_host=localhost", "--app_port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args, Prefix = "app_" })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void NoPrefix_ReturnsAllArguments()
    {
        var args = new[] { "--host=localhost", "--port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void Prefix_NoMatchingArgs_ReturnsEmptyConfig()
    {
        var args = new[] { "--other_host=localhost", "--other_port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions { Args = args, Prefix = "app_" })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Null(cfg!.Host);
        Assert.Equal(0, cfg.Port);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void MultipleSwitchPrefixes_ParsesCorrectly()
    {
        var args = new[] { "--host=localhost", "-port=8080", "/debug=true" };
        using var manager = new ConfigManager(rule => [rule.For<MixedConfig>().FromCommandLine(cm => new CommandLineRuleOptions
        {
            Args = args,
            SwitchPrefixes = ["--", "-", "/"]
        })]);
        manager.Initialize();

        var cfg = manager.GetConfig<MixedConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
        Assert.True(cfg.Debug);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void MultipleSwitchPrefixes_WithSpaceFormat_ParsesCorrectly()
    {
        var args = new[] { "--host", "localhost", "-port", "8080", "/debug" };
        using var manager = new ConfigManager(rule => [rule.For<MixedConfig>().FromCommandLine(cm => new CommandLineRuleOptions
        {
            Args = args,
            SwitchPrefixes = ["--", "-", "/"]
        })]);
        manager.Initialize();

        var cfg = manager.GetConfig<MixedConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);
        Assert.Equal(8080, cfg.Port);
        Assert.True(cfg.Debug);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "CommandLineArgumentProvider")]
    public void MultipleSwitchPrefixes_LongestMatchFirst_ParsesCorrectly()
    {
        // Ensure "--" matches before "-" even if array order is reversed
        var args = new[] { "--host=localhost", "-port=8080" };
        using var manager = new ConfigManager(rule => [rule.For<SimpleConfig>().FromCommandLine(cm => new CommandLineRuleOptions
        {
            Args = args,
            SwitchPrefixes = ["-", "--"]  // Note: shorter prefix first in array
        })]);
        manager.Initialize();

        var cfg = manager.GetConfig<SimpleConfig>();
        Assert.NotNull(cfg);
        Assert.Equal("localhost", cfg!.Host);  // Should match "--", not "-" (which would give "-host")
        Assert.Equal(8080, cfg.Port);
    }
}




