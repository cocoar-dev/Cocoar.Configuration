using System;
using System.Text.Json;
using System.Threading.Tasks;
using Cocoar.Configuration;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Providers;
using Xunit;

namespace Cocoar.Configuration.Core.Tests.Health;

/// <summary>
/// ConfigManagerHealthIntegrationTests
/// -----------------------------------
/// PURPOSE
///   Integration tests for ConfigManager health monitoring system validating
///   health status aggregation across multiple providers and health report
///   generation under various operational scenarios.
/// 
/// SCOPE
///   - Health status aggregation (Healthy/Degraded/Unhealthy)
///   - Health check integration with ConfigManager lifecycle
///   - Multi-provider health scenario testing
///   - Health report generation and structure validation
/// 
/// COVERAGE
///   - Static provider health integration
///   - Observable provider health monitoring
///   - Health status transitions and reporting
///   - Health check timing and lifecycle coordination
/// 
/// CONSTRAINTS
///   - Uses ONLY StaticJsonProvider and ObservableProvider (Core.Tests architecture)
///   - Focuses on health system integration, not provider-specific health logic
///   - Validates health aggregation behavior across provider combinations
/// </summary>
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
        // Arrange
        using var document = JsonDocument.Parse("{}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        var rule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new ConfigRuleOptions(Required: false));

        // Act - Call GetHealth() BEFORE Initialize()
        using var configManager = new ConfigManager([rule]);
    var health = configManager.GetHealthService().Snapshot;

        // Assert - Check the start/initial values
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
        // Arrange
        using var document = JsonDocument.Parse("{\"Name\":\"test\",\"Value\":123}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        var rule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new ConfigRuleOptions(Required: false));

        using var configManager = new ConfigManager([rule]);
    var healthUpdates = new List<ConfigHealthSnapshot>();

        // Act
        using var subscription = configManager.GetHealthService().SnapshotStream
            .Subscribe(s => healthUpdates.Add(s));

        configManager.Initialize();

        // Give it a moment for the observable to emit
        await Task.Delay(50);

        // Assert
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
        // Arrange
        using var document = JsonDocument.Parse("{\"Name\":\"test\",\"Value\":123}");
        var providerOptions = new StaticJsonProviderOptions(document.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        var rule = new ConfigRule(typeof(StaticJsonProvider), providerOptions, queryOptions, typeof(SimpleConfig),
            new ConfigRuleOptions(Required: false));

        // Act
        using var configManager = new ConfigManager([rule]);
        configManager.Initialize();
    var health = configManager.GetHealthService().Snapshot;

        // Assert
    Assert.Equal(HealthStatus.Healthy, health.OverallStatus);
        Assert.Single(health.Rules);
    Assert.Equal(RuleResultStatus.Up, health.Rules[0].Status);
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Area", "Health")]
    public void ConfigManager_WithRequiredAndOptionalRules_ShowsCorrectHealth()
    {
        // Arrange
        using var requiredDocument = JsonDocument.Parse("{\"Name\":\"required\",\"Value\":1}");
        using var optionalDocument = JsonDocument.Parse("{\"Name\":\"optional\",\"Value\":2}");
        var requiredProviderOptions = new StaticJsonProviderOptions(requiredDocument.RootElement);
        var optionalProviderOptions = new StaticJsonProviderOptions(optionalDocument.RootElement);
        var queryOptions = new StaticJsonProviderQueryOptions();
        
        var requiredRule = new ConfigRule(typeof(StaticJsonProvider), requiredProviderOptions, queryOptions, typeof(SimpleConfig),
            new ConfigRuleOptions(Required: true));

        var optionalRule = new ConfigRule(typeof(StaticJsonProvider), optionalProviderOptions, queryOptions, typeof(SimpleConfig),
            new ConfigRuleOptions(Required: false));

        // Act
        using var configManager = new ConfigManager([requiredRule, optionalRule]);
        configManager.Initialize();
    var health = configManager.GetHealthService().Snapshot;

        // Assert
    Assert.Equal(HealthStatus.Healthy, health.OverallStatus);
        Assert.Equal(2, health.Rules.Count);
        
        // Both should be successful
    Assert.All(health.Rules, rule => Assert.Equal(RuleResultStatus.Up, rule.Status));
        
        // Check required vs optional
    Assert.Contains(health.Rules, r => r.Required);
    Assert.Contains(health.Rules, r => !r.Required);
    }
}