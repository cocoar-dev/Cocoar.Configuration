using System.Text.Json;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Health;

using Cocoar.Configuration.Core.Tests.Helpers;
using Cocoar.Configuration.Core.Tests.TestUtilities;

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

        // Wait for the observable to emit health update
        await ActiveWaitHelpers.WaitUntilAsync(
            () => healthUpdates.Count >= 1,
            timeout: TimeSpan.FromSeconds(2),
            description: "initial health update emission");

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

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public void ConfigManager_WithNamedRules_ShowsNamesInHealth()
    {
        using var document1 = JsonDocument.Parse("{\"Name\":\"config1\",\"Value\":1}");
        using var document2 = JsonDocument.Parse("{\"Name\":\"config2\",\"Value\":2}");
        var providerOptions1 = new StaticJsonProviderOptions(document1.RootElement);
        var providerOptions2 = new StaticJsonProviderOptions(document2.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        
        var namedRule = new ConfigRule(typeof(StaticJsonProvider), providerOptions1, queryOptions, typeof(SimpleConfig),
            new(Required: true, Name: "Primary Config"));

        var unnamedRule = new ConfigRule(typeof(StaticJsonProvider), providerOptions2, queryOptions, typeof(SimpleConfig),
            new(Required: false));

        using var configManager = new ConfigManager(new[] {namedRule, unnamedRule});
        configManager.Initialize();
        var health = configManager.GetHealthService().Snapshot;

        Assert.Equal(HealthStatus.Healthy, health.OverallStatus);
        Assert.Equal(2, health.Rules.Count);
        
        // Check that explicit name is used
        var rule1 = health.Rules[0];
        Assert.Equal("Primary Config", rule1.Name);
        Assert.True(rule1.Required);
        
        // Check that no name results in null
        var rule2 = health.Rules[1];
        Assert.Null(rule2.Name);
        Assert.False(rule2.Required);
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public void ConfigManager_WithNameAndMountPath_NameIsIndependentOfMountPath()
    {
        using var document = JsonDocument.Parse("{\"Name\":\"test\",\"Value\":123}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        
        // Both name and mount path provided - they are independent
        var rule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new(Required: false, Name: "Explicit Name", MountPath: "MountPath"));

        using var configManager = new ConfigManager(new[] {rule});
        configManager.Initialize();
        var health = configManager.GetHealthService().Snapshot;

        Assert.Single(health.Rules);
        var ruleHealth = health.Rules[0];
        Assert.Equal("Explicit Name", ruleHealth.Name);
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public void ConfigManager_WithConditionalRule_ShowsSkippedStatus()
    {
        using var document = JsonDocument.Parse("{\"Name\":\"test\",\"Value\":123}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        
        // Rule with UseWhen that returns false - should be skipped
        var conditionalRule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new(Required: false, UseWhen: _ => false, Name: "Conditional Rule"));

        var alwaysRule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new(Required: false, Name: "Always Rule"));

        using var configManager = new ConfigManager(new[] {conditionalRule, alwaysRule});
        configManager.Initialize();
        var health = configManager.GetHealthService().Snapshot;

        Assert.Equal(HealthStatus.Healthy, health.OverallStatus);
        Assert.Equal(2, health.Rules.Count);
        
        // First rule should be skipped
        var rule1 = health.Rules[0];
        Assert.Equal("Conditional Rule", rule1.Name);
        Assert.Equal(RuleResultStatus.Skipped, rule1.Status);
        
        // Second rule should be up
        var rule2 = health.Rules[1];
        Assert.Equal("Always Rule", rule2.Name);
        Assert.Equal(RuleResultStatus.Up, rule2.Status);
        
        // Check summary
        Assert.Equal(1, health.Summary.Skipped);
    }
}



