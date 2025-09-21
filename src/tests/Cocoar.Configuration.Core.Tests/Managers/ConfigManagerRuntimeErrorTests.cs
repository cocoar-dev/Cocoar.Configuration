using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;

namespace Cocoar.Configuration.Core.Tests.Managers;

/// <summary>
/// Tests runtime behavior when required rules fail during recompute (vs initialization).
/// Validates the difference between initialization failures and runtime failures.
/// </summary>
public class ConfigManagerRuntimeErrorTests : IDisposable
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
    /// Tests that initialization fails immediately when a required provider always fails.
    /// This represents the expected behavior - required rules must succeed during startup.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_RequiredRuleAlwaysFails_ThrowsDuringInitialization()
    {
        // Arrange: Provider configured to always fail
        var options = FailableProviderOptions.AlwaysFail(
            json: """{"Name": "WontWork", "Value": 999}""");

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                options,
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new ConfigRuleOptions(Required: true)) // Required rule
        };

        // Act & Assert: Should throw during initialization
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
            configManager.Initialize();
        });

        Assert.Contains("Required rule failed for FailableProvider", exception.Message);
        // The inner exception should contain our specific failure message
        Assert.NotNull(exception.InnerException);
        Assert.Contains("FailableProvider configured to fail", exception.InnerException.Message);
    }

    /// <summary>
    /// Tests the "file corruption after startup" scenario - provider succeeds initially 
    /// but would fail on subsequent calls. Since we can't easily trigger recompute 
    /// in this test setup, this demonstrates the failure pattern.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_RuntimeFailureSimulation_InitialSuccessThenFailure()
    {
        // Arrange Part 1: Provider that succeeds initially
        var successOptions = FailableProviderOptions.AlwaysSucceed(
            json: """{"Name": "InitialGood", "Value": 100}""");

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                successOptions,
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new ConfigRuleOptions(Required: true))
        };

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);

        // Act Part 1: Initialize successfully
        configManager.Initialize();
        var initialConfig = configManager.GetConfig<TestConfig>();

        // Assert Part 1: Should work fine initially
        Assert.NotNull(initialConfig);
        Assert.Equal("InitialGood", initialConfig.Name);
        Assert.Equal(100, initialConfig.Value);

        // Arrange Part 2: Simulate what would happen if the same provider started failing
        // (In real scenarios, this would happen during recompute due to file corruption)
        var failingOptions = FailableProviderOptions.AlwaysFail(
            json: """{"Name": "WontWork", "Value": 999}""");

        var failingRules = new List<ConfigRule>
        {
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                failingOptions,
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new ConfigRuleOptions(Required: true))
        };

        // Act Part 2: Try to create a new ConfigManager with the failing provider
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var failingConfigManager = new ConfigManager(failingRules, logger: NullLogger.Instance);
            failingConfigManager.Initialize();
        });

        // Assert Part 2: Should fail with expected error
        Assert.Contains("Required rule failed for FailableProvider", exception.Message);
        Assert.NotNull(exception.InnerException);
        Assert.Contains("FailableProvider configured to fail", exception.InnerException.Message);
        
        // The original configManager should still have the good config
        var stillGoodConfig = configManager.GetConfig<TestConfig>();
        Assert.NotNull(stillGoodConfig);
        Assert.Equal("InitialGood", stillGoodConfig.Name);
    }

    /// <summary>
    /// Tests provider that fails after N successful calls - simulates progressive failure.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "Medium")]
    public async Task ConfigManager_FailAfterNCalls_BehavesAsExpected()
    {
        // Arrange: Provider that fails after 1 successful call
        var options = FailableProviderOptions.FailAfterNCalls(
            json: """{"Name": "ProgressiveFailure", "Value": 200}""",
            callsBeforeFailure: 1);

        // Create a provider instance directly to test the call counting
        var provider = new FailableProvider(options);
        var query = FailableProviderQuery.Success;

        // Act & Assert: First call should succeed
        var firstResult = await provider.FetchConfigurationAsync(query);
        Assert.Equal("ProgressiveFailure", firstResult.GetProperty("Name").GetString());
        Assert.Equal(200, firstResult.GetProperty("Value").GetInt32());

        // Act & Assert: Second call should fail
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.FetchConfigurationAsync(query));
        
        Assert.Contains("FailableProvider configured to fail", exception.Message);
        Assert.Contains("AfterNCalls", exception.Message);
        Assert.Contains("Call: 2", exception.Message);
    }
}