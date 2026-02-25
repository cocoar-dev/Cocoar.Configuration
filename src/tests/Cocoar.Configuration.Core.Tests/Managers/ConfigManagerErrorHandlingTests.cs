using Cocoar.Configuration.Core.Tests.Helpers;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.Configuration.Core.Tests.Managers;

public class ConfigManagerErrorHandlingTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignore disposal errors in tests
            }
        }
        _disposables.Clear();
    }

    private void TrackForDisposal(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    public class TestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_RequiredRuleFails_ThrowsInvalidOperationException()
    {

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled("""{"Name": "Test"}"""),
                FailableProviderQuery.Failure, // This will cause the provider to fail
                typeof(TestConfig),
                new(Required: true))
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseLogger(NullLogger.Instance));
            TrackForDisposal(configManager);
        });

        // Verify exception message contains provider information
        Assert.Contains("Required rule failed", exception.Message);
        Assert.Contains("FailableProvider", exception.Message);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_OptionalRuleFails_ContinuesProcessing()
    {

        var successData = """{"Name": "Success", "Value": 42}""";

        var rules = new List<ConfigRule>
        {
            // First rule: Optional and will fail - should be skipped
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled("""{"Name": "Fail"}"""),
                FailableProviderQuery.Failure,
                typeof(TestConfig),
                new(Required: false)), // Optional rule
            
            // Second rule: Will succeed - should be used
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled(successData),
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: false))
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("Success", config.Name);
        Assert.Equal(42, config.Value);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "Medium")]
    public void ConfigManager_RequiredSucceedsOptionalFails_UsesRequiredRule()
    {

        var requiredData = """{"Name": "Required", "Value": 100}""";

        var rules = new List<ConfigRule>
        {
            // First rule: Required and will succeed - should be used
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled(requiredData),
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: true)), // Required rule
            
            // Second rule: Optional and will fail - should be skipped
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled("""{"Name": "OptionalFail"}"""),
                FailableProviderQuery.Failure,
                typeof(TestConfig),
                new(Required: false)) // Optional rule
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("Required", config.Name);
        Assert.Equal(100, config.Value);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "Medium")]
    public void ConfigManager_MultipleOptionalRulesFail_SkipsAllAndContinues()
    {

        var successData = """{"Name": "OnlySuccess", "Value": 999}""";

        var rules = new List<ConfigRule>
        {
            // Multiple optional rules that will fail
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled("""{"Name": "Fail1"}"""),
                new(true, "First failure"),
                typeof(TestConfig),
                new(Required: false)),

            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled("""{"Name": "Fail2"}"""),
                new(true, "Second failure"),
                typeof(TestConfig),
                new(Required: false)),
            
            // One successful rule at the end
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.QueryControlled(successData),
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: false))
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("OnlySuccess", config.Name);
        Assert.Equal(999, config.Value);
    }
}
