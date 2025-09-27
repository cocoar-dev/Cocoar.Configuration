using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Core.Tests.TestUtilities;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Managers;

/// <summary>
/// Tests error handling behavior for ConfigManager with required vs optional rules.
/// Validates that required rule failures crash the application during initialization,
/// while optional rule failures are gracefully skipped.
/// </summary>
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

    /// <summary>
    /// Tests that required rule failures cause ConfigManager initialization to throw.
    /// This validates the fail-fast behavior for critical configuration dependencies.
    /// </summary>
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

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);


        var exception = Assert.Throws<InvalidOperationException>(() => configManager.Initialize());
        
        // Verify exception message contains provider information
        Assert.Contains("Required rule failed", exception.Message);
        Assert.Contains("FailableProvider", exception.Message);
    }

    /// <summary>
    /// Tests that ConfigManager skips optional rules that fail and continues processing other rules.
    /// This validates that optional rule failures don't break the entire configuration.
    /// </summary>
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

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);


        configManager.Initialize();
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("Success", config.Name);
        Assert.Equal(42, config.Value);
    }

    /// <summary>
    /// Tests mixed scenarios where required rules succeed but optional rules fail.
    /// Validates that required rule data takes precedence over optional failures.
    /// </summary>
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

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);


        configManager.Initialize();
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("Required", config.Name);
        Assert.Equal(100, config.Value);
    }

    /// <summary>
    /// Tests that multiple optional rule failures are all skipped gracefully.
    /// Validates robust error handling when multiple rules fail.
    /// </summary>
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

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);


        configManager.Initialize();
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("OnlySuccess", config.Name);
        Assert.Equal(999, config.Value);
    }
}
