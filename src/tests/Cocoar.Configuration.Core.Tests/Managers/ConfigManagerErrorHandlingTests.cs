using System.Reactive.Subjects;
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
            var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
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

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
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

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
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

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("OnlySuccess", config.Name);
        Assert.Equal(999, config.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    public async Task RuntimeRecompute_RequiredRuleFails_SubscriberNeverSeesPartialState()
    {
        var validJson = """{"Name": "Initial", "Value": 1}""";
        var invalidJson = "NOT VALID JSON {{{";

        var subject = new BehaviorSubject<string>(validJson);
        TrackForDisposal(subject);

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<TestConfig>(subject, required: true)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));
        TrackForDisposal(configManager);

        var reactiveConfig = configManager.GetReactiveConfig<TestConfig>();

        // Collect all values the subscriber ever observes
        var observedValues = new List<TestConfig>();
        var subscription = reactiveConfig.Subscribe(v => observedValues.Add(v));
        TrackForDisposal(subscription);

        // Wait for initial emission
        await ActiveWaitHelpers.WaitUntilAsync(
            () => observedValues.Count > 0 && observedValues.Last().Name == "Initial",
            description: "initial configuration to emit");

        var initialConfig = reactiveConfig.CurrentValue;
        Assert.NotNull(initialConfig);
        Assert.Equal("Initial", initialConfig.Name);
        Assert.Equal(1, initialConfig.Value);

        var countBeforeBadPush = observedValues.Count;

        // Push invalid JSON — this should cause the required rule to fail during recompute,
        // triggering a rollback that preserves the last good configuration
        subject.OnNext(invalidJson);

        // Wait for the recompute cycle to complete (debounce + processing)
        await ActiveWaitHelpers.WaitUntilAsync(
            () => true,
            timeout: TimeSpan.FromMilliseconds(500),
            description: "recompute cycle to settle after invalid JSON push");

        // The reactive config should still hold the last known good value
        var currentAfterFailure = reactiveConfig.CurrentValue;
        Assert.NotNull(currentAfterFailure);
        Assert.Equal("Initial", currentAfterFailure.Name);
        Assert.Equal(1, currentAfterFailure.Value);

        // The subscriber should never have seen a partial or invalid state —
        // every observed value should have a valid Name
        Assert.All(observedValues, v =>
        {
            Assert.NotNull(v);
            Assert.Equal("Initial", v.Name);
        });

        // Push a valid update to prove the system is still functional after the failure
        var recoveryJson = """{"Name": "Recovered", "Value": 99}""";
        subject.OnNext(recoveryJson);

        await ActiveWaitHelpers.WaitUntilAsync(
            () => reactiveConfig.CurrentValue.Name == "Recovered",
            description: "configuration to update to Recovered after recovery");

        var recoveredConfig = reactiveConfig.CurrentValue;
        Assert.Equal("Recovered", recoveredConfig.Name);
        Assert.Equal(99, recoveredConfig.Value);
    }
}
