using Cocoar.Configuration.Fluent;

using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;
using Cocoar.Configuration.Providers.FileSourceProvider;

namespace Cocoar.Configuration.Tests.Fluent;

[Collection("EnvironmentTests")] // Prevent parallel execution with other env tests
public class FluentBuilderTests
{
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
    public void FluentAPI_FileRule_LoadsJsonSection_Successfully()
    {
        // Arrange
        var config1 = Path.GetFullPath(Path.Combine("TestConfigFiles", "config1.json"));

        var rule = Rule.From
            .File(_ => FileSourceRuleOptions.FromFilePath(config1)).Select("SectionA")
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
    public void FluentAPI_FileAndEnvironment_EnvironmentOverridesFile_Successfully()
    {
        // Arrange temp file SectionA.Enabled=true
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "{ \"SectionA\": { \"Enabled\": true } }");

        var key = "Enabled"; // environment variable intended to override SectionA.Enabled
        Environment.SetEnvironmentVariable(key, "false");

        try
        {
            var fileRule = Rule.From
                .File(_ => FileSourceRuleOptions.FromFilePath(tempFile)).Select("SectionA")
                .For<TestClass>()
                .As<IMySectionSettings>()
                .Build();

            var envRule = Rule.From
                .Environment(_ => new EnvironmentVariableRuleOptions(environmentPrefix: null))
                .For<TestClass>()
                .As<IMySectionSettings>()
                .Build();

            var services = new ServiceCollection();
            services.AddCocoarConfiguration(fileRule, envRule);
            var sp = services.BuildServiceProvider();

            // Act
            var manager = sp.GetRequiredService<ConfigManager>();
            var cfg = manager.GetConfig<IMySectionSettings>();

            // Assert: env overrides file
            Assert.NotNull(cfg);
            Assert.False(cfg.Enabled);
        }
        finally
        {
            File.Delete(tempFile);
            Environment.SetEnvironmentVariable(key, null);
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
                .File(_ => FileSourceRuleOptions.FromFilePath(tempFile, TimeSpan.FromMilliseconds(50)))
                .Select("SectionA")
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
