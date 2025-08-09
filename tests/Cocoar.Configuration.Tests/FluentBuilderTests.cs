using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Fluent.ProviderOptions;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Extensions.Tests;

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
    public void Fluent_File_Rule_Loads_Section()
    {
        // Arrange
        var config1 = Path.GetFullPath(Path.Combine("TestConfigFiles", "config1.json"));

        var rule = Rules
            .FromFile(_ => FileSourceRuleOptions.FromFilePath(config1, "SectionA"))
            .ForType<TestClass>()
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
        // Arrange temp file SectionA.Enabled=true
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "{ \"SectionA\": { \"Enabled\": true } }");
        var key = "Enabled";
        Environment.SetEnvironmentVariable(key, "false");

        try
        {
            var fileRule = Rules
                .FromFile(_ => FileSourceRuleOptions.FromFilePath(tempFile, "SectionA"))
                .ForType<TestClass>()
                .As<IMySectionSettings>()
                .Build();

            var envRule = Rules
                .FromEnvironment(_ => new EnvironmentVariableRuleOptions(keyPrefix: null, wrapperPath: null))
                .ForType<TestClass>()
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
            Assert.False(cfg!.Enabled);
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
            var rule = Rules
                .FromFile(_ => FileSourceRuleOptions.FromFilePath(tempFile, "SectionA", null, TimeSpan.FromMilliseconds(50)))
                .ForType<TestClass>()
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
