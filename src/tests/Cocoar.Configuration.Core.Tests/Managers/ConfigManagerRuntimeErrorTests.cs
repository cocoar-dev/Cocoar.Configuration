using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Core.Tests.TestUtilities;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Managers;
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
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_RequiredRuleAlwaysFails_ThrowsDuringInitialization()
    {

        var options = FailableProviderOptions.AlwaysFail(
            json: """{"Name": "WontWork", "Value": 999}""");

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                options,
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: true)) // Required rule
        };


        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            ConfigManager.Create(c => c.WithConfiguration(rules).UseLogger(NullLogger.Instance));
        });

        Assert.Contains("Required rule failed for FailableProvider", exception.Message);
        // The inner exception should contain our specific failure message
        Assert.NotNull(exception.InnerException);
        Assert.Contains("FailableProvider configured to fail", exception.InnerException.Message);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_RuntimeFailureSimulation_InitialSuccessThenFailure()
    {

        var successOptions = FailableProviderOptions.AlwaysSucceed(
            json: """{"Name": "InitialGood", "Value": 100}""");

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                successOptions,
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: true))
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);
        var initialConfig = configManager.GetConfig<TestConfig>();


        Assert.NotNull(initialConfig);
        Assert.Equal("InitialGood", initialConfig.Name);
        Assert.Equal(100, initialConfig.Value);


        // (In real scenarios, this would happen during recompute due to file corruption)
        var failingOptions = FailableProviderOptions.AlwaysFail(
            json: """{"Name": "WontWork", "Value": 999}""");

        var failingRules = new List<ConfigRule>
        {
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                failingOptions,
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: true))
        };


        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            ConfigManager.Create(c => c.WithConfiguration(failingRules).UseLogger(NullLogger.Instance));
        });


        Assert.Contains("Required rule failed for FailableProvider", exception.Message);
        Assert.NotNull(exception.InnerException);
        Assert.Contains("FailableProvider configured to fail", exception.InnerException.Message);
        
        // The original configManager should still have the good config
        var stillGoodConfig = configManager.GetConfig<TestConfig>();
        Assert.NotNull(stillGoodConfig);
        Assert.Equal("InitialGood", stillGoodConfig.Name);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "Medium")]
    public async Task ConfigManager_FailAfterNCalls_BehavesAsExpected()
    {

        var options = FailableProviderOptions.FailAfterNCalls(
            json: """{"Name": "ProgressiveFailure", "Value": 200}""",
            callsBeforeFailure: 1);

        // Create a provider instance directly to test the call counting
        var provider = new FailableProvider(options);
        var query = FailableProviderQuery.Success;


        var firstResult = await provider.FetchConfigurationBytesAsync(query);
        Assert.Equal("ProgressiveFailure", firstResult.ToJsonElement().GetProperty("Name").GetString());
        Assert.Equal(200, firstResult.ToJsonElement().GetProperty("Value").GetInt32());


        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.FetchConfigurationBytesAsync(query));
        
        Assert.Contains("FailableProvider configured to fail", exception.Message);
        Assert.Contains("AfterNCalls", exception.Message);
        Assert.Contains("Call: 2", exception.Message);
    }
}