using System.Text.Json;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Health;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Health;

public class ConfigManagerHealthIntegrationTests
{
    private class SimpleConfig
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public void ConfigManager_GetHealth_ReturnsInitialHealthInfo()
    {
        using var document = JsonDocument.Parse("{}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        var rule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new(Required: false));

        using var configManager = new ConfigManager(new[] {rule});
        var health = configManager.GetHealthService().Snapshot;

        Assert.NotNull(health);
        Assert.Equal(HealthStatus.Unknown, health.OverallStatus);
        Assert.Single(health.Rules);
        Assert.Equal(RuleResultStatus.Unknown, health.Rules[0].Status);
        Assert.False(health.Rules[0].Required);
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public async Task ConfigManager_GetHealthObservable_EmitsHealthUpdates()
    {
        using var document = JsonDocument.Parse("{\"Name\":\"test\",\"Value\":123}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        var rule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new(Required: false));

        using var configManager = new ConfigManager(new[] {rule});
        var healthUpdates = new List<ConfigHealthSnapshot>();

        using var subscription = configManager.GetHealthService().SnapshotStream
            .Subscribe(s => healthUpdates.Add(s));

        configManager.Initialize();

        // Give it a moment for the observable to emit
        await Task.Delay(50);

        Assert.True(healthUpdates.Count >= 1);
        
        // Check that we get health updates
        var latestHealth = configManager.GetHealthService().Snapshot;
        Assert.Single(latestHealth.Rules);
        var ruleHealth = latestHealth.Rules[0];
        Assert.Equal(RuleResultStatus.Up, ruleHealth.Status);
        Assert.False(ruleHealth.Required);
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public void ConfigManager_InitializeWithValidConfig_ShowsHealthyState()
    {
        using var document = JsonDocument.Parse("{\"Name\":\"test\",\"Value\":123}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        var rule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new(Required: false));

        using var configManager = new ConfigManager(new[] {rule});
        configManager.Initialize();
        var health = configManager.GetHealthService().Snapshot;

        Assert.Equal(HealthStatus.Healthy, health.OverallStatus);
        Assert.Single(health.Rules);
        Assert.Equal(RuleResultStatus.Up, health.Rules[0].Status);
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public void ConfigManager_WithRequiredAndOptionalRules_ShowsCorrectHealth()
    {
        using var requiredDocument = JsonDocument.Parse("{\"Name\":\"required\",\"Value\":1}");
        using var optionalDocument = JsonDocument.Parse("{\"Name\":\"optional\",\"Value\":2}");
        var requiredProviderOptions = new StaticJsonProviderOptions(requiredDocument.RootElement);
        var optionalProviderOptions = new StaticJsonProviderOptions(optionalDocument.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        
        var requiredRule = new ConfigRule(typeof(StaticJsonProvider), requiredProviderOptions, queryOptions, typeof(SimpleConfig),
            new(Required: true));

        var optionalRule = new ConfigRule(typeof(StaticJsonProvider), optionalProviderOptions, queryOptions, typeof(SimpleConfig),
            new(Required: false));

        using var configManager = new ConfigManager(new[] {requiredRule, optionalRule});
        configManager.Initialize();
        var health = configManager.GetHealthService().Snapshot;

        Assert.Equal(HealthStatus.Healthy, health.OverallStatus);
        Assert.Equal(2, health.Rules.Count);
        
        // Both should be successful
        Assert.All(health.Rules, rule => Assert.Equal(RuleResultStatus.Up, rule.Status));
        
        // Check required vs optional
        Assert.Contains(health.Rules, r => r.Required);
        Assert.Contains(health.Rules, r => !r.Required);
    }
}
