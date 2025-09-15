using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Tests.Core;

[Collection("EnvironmentTests")] // Prevent parallel execution with other env tests
public class ConfigManagerIntegrationTests
{
    public interface IMySectionSettings
    {
        bool Enabled { get; }
    }

    public class TestClass : IMySectionSettings
    {
        public bool Enabled { get; set; }
        public int Value { get; set; } = 2;
        public string StringValue { get; set; } = "Leer";
    }

    [Fact]
    public void ConfigManager_Merges_File_And_EnvironmentVariableProvider()
    {
        var tempPath = Path.GetTempFileName();
        var key = "Enabled";
        var envValue = "false";
        var fileValue = true;

        // Write file with Enabled = true
        File.WriteAllText(tempPath, $"{{ \"SectionA\": {{ \"Enabled\": {fileValue.ToString().ToLower()} }} }}");

        // Set environment variable to override
        Environment.SetEnvironmentVariable(key, envValue);

        var services = new ServiceCollection();
        services.AddCocoarConfiguration([
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath)).Select("SectionA").For<TestClass>()
                .As<IMySectionSettings>(),
            Rule.From.Environment(_ => new EnvironmentVariableRuleOptions()).For<TestClass>().As<IMySectionSettings>()
        ]);

        var sp = services.BuildServiceProvider();

        try
        {
            var manager = sp.GetRequiredService<ConfigManager>();
            var result = manager.GetConfig<IMySectionSettings>();
            Assert.NotNull(result);
            // Should be overwritten by env var ("false")
            Assert.False(result.Enabled);
        }
        finally
        {
            File.Delete(tempPath);
            Environment.SetEnvironmentVariable(key, null); // cleanup
        }
    }
}
