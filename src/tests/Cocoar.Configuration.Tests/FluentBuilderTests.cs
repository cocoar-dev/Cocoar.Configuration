using Cocoar.Configuration.Fluent;

using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using System.Runtime.InteropServices;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;
using Cocoar.Configuration.Providers.FileSourceProvider;

namespace Cocoar.Configuration.Tests;

public class FluentBuilderTests
{
    private readonly ITestOutputHelper? _output;

    public FluentBuilderTests(ITestOutputHelper? output = null)
    {
        _output = output;
    }

    private void Log(string message)
    {
        // Write to both xUnit output and Console so logs are visible locally and in CI
        _output?.WriteLine(message);
        Console.WriteLine(message);
    }
    public interface IMySectionSettings
    {
        bool Enabled { get; }
    }

    public class TestClass : IMySectionSettings
    {
        public bool Enabled { get; set; }
        public int Value { get; set; }
    }

    [Fact]
    public void Fluent_File_Rule_Loads_Section()
    {
        // Arrange
        var config1 = Path.GetFullPath(Path.Combine("TestConfigFiles", "config1.json"));

        var rule = Rule.From
            .File(_ => FileSourceRuleOptions.FromFilePath(config1, "SectionA"))
            .For<TestClass>()
            .Build();

        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rule);
        var sp = services.BuildServiceProvider();

        // Act
        var manager = sp.GetRequiredService<ConfigManager>();
        var cfg = manager.GetConfig<TestClass>();

        // Assert (config1 SectionA.Enabled is true in test data)
        Assert.True(cfg!.Enabled);
    }

    [Fact]
    public void Fluent_File_Then_Env_Overlay_With_Interface_Resolution()
    {
    Log("=== Begin Fluent_File_Then_Env_Overlay_With_Interface_Resolution ===");
    Log($"OS: {Environment.OSVersion}; IsWindows={RuntimeInformation.IsOSPlatform(OSPlatform.Windows)}; IsLinux={RuntimeInformation.IsOSPlatform(OSPlatform.Linux)}");

        // Arrange temp file SectionA.Enabled=true
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "{ \"SectionA\": { \"Enabled\": true } }");
    Log($"Temp file: {tempFile}");
    Log($"Temp file content: {File.ReadAllText(tempFile)}");

    var key = "Enabled"; // environment variable intended to override SectionA.Enabled
    var keyUpper = key.ToUpperInvariant();
    var beforeExact = Environment.GetEnvironmentVariable(key);
    var beforeUpper = Environment.GetEnvironmentVariable(keyUpper);
    Log($"Env before set: {key}='{beforeExact ?? "<null>"}', {keyUpper}='{beforeUpper ?? "<null>"}'");

    Environment.SetEnvironmentVariable(key, "false");
    var afterExact = Environment.GetEnvironmentVariable(key);
    var afterUpper = Environment.GetEnvironmentVariable(keyUpper);
    Log($"Env after set:  {key}='{afterExact ?? "<null>"}', {keyUpper}='{afterUpper ?? "<null>"}'");

        try
        {
            var fileRule = Rule.From
                .File(_ => FileSourceRuleOptions.FromFilePath(tempFile, "SectionA"))
                .For<TestClass>()
                .AsSingleton<IMySectionSettings>()
                .Build();

            var envRule = Rule.From
                .Environment(_ => new EnvironmentVariableRuleOptions(environmentPrefix: null, targetPath: null))
                .For<TestClass>()
                .AsSingleton<IMySectionSettings>()
                .Build();

            // Probe each rule independently to observe values
            var servicesFileOnly = new ServiceCollection();
            servicesFileOnly.AddCocoarConfiguration(fileRule);
            var spFile = servicesFileOnly.BuildServiceProvider();
            var mgrFile = spFile.GetRequiredService<ConfigManager>();
            var cfgFileOnly = mgrFile.GetConfig<IMySectionSettings>();
            Log($"Value from FILE only: Enabled={(cfgFileOnly != null ? cfgFileOnly.Enabled.ToString() : "<null>")}");

            var servicesEnvOnly = new ServiceCollection();
            servicesEnvOnly.AddCocoarConfiguration(envRule);
            var spEnv = servicesEnvOnly.BuildServiceProvider();
            var mgrEnv = spEnv.GetRequiredService<ConfigManager>();
            var cfgEnvOnly = mgrEnv.GetConfig<IMySectionSettings>();
            Log($"Value from ENV only:  Enabled={(cfgEnvOnly != null ? cfgEnvOnly.Enabled.ToString() : "<null>")}");

            var services = new ServiceCollection();
            services.AddCocoarConfiguration(fileRule, envRule);
            var sp = services.BuildServiceProvider();

            // Act
            var manager = sp.GetRequiredService<ConfigManager>();
            var cfg = manager.GetConfig<IMySectionSettings>();
            Log($"Value from COMBINED (env over file): Enabled={(cfg != null ? cfg.Enabled.ToString() : "<null>")}");

            // Assert: env overrides file
            Assert.NotNull(cfg);
            Assert.False(cfg!.Enabled);
        }
        finally
        {
            File.Delete(tempFile);
            Environment.SetEnvironmentVariable(key, null);
            var afterCleanup = Environment.GetEnvironmentVariable(key);
            Log($"Env after cleanup: {key}='{afterCleanup ?? "<null>"}'");
            Log("=== End Fluent_File_Then_Env_Overlay_With_Interface_Resolution ===");
        }
    }

    [Fact]
    public void Fluent_File_WithOptions_Lambda_Works()
    {
        // Arrange temp file SectionA.Enabled=false
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "{ \"SectionA\": { \"Enabled\": false } }");
        try
        {
            var rule = Rule.From
                .File(_ => FileSourceRuleOptions.FromFilePath(tempFile, "SectionA", null, TimeSpan.FromMilliseconds(50)))
                .For<TestClass>()
                .Build();

            var services = new ServiceCollection();
            services.AddCocoarConfiguration(rule);
            var sp = services.BuildServiceProvider();

            var manager = sp.GetRequiredService<ConfigManager>();
            var cfg = manager.GetConfig<TestClass>();
            Assert.False(cfg!.Enabled);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
