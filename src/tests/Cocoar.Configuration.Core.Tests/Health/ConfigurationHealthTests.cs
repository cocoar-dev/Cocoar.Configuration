using Cocoar.Configuration.Health;
using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Health;

public class ConfigurationHealthTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void HealthStatus_HasExpectedValues()
    {
        Assert.Equal(0, (int)HealthStatus.Unknown);
        Assert.Equal(1, (int)HealthStatus.Healthy);
        Assert.Equal(2, (int)HealthStatus.Degraded);
        Assert.Equal(3, (int)HealthStatus.Unhealthy);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void RuleResultStatus_HasExpectedValues()
    {
        Assert.Equal(0, (int)RuleResultStatus.Unknown);
        Assert.Equal(1, (int)RuleResultStatus.Up);
        Assert.Equal(2, (int)RuleResultStatus.Down);
        Assert.Equal(3, (int)RuleResultStatus.Skipped);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void RuleHealthInfo_CanBeCreated()
    {
        var ruleHealth = new RuleHealthInfo(
            RuleResultStatus.Up,
            IsRequired: true,
            IsSkipped: false
        );

        Assert.Equal(RuleResultStatus.Up, ruleHealth.Status);
        Assert.True(ruleHealth.IsRequired);
        Assert.False(ruleHealth.IsSkipped);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_CanBeCreated()
    {
        var rules = new[]
        {
            new RuleHealthInfo(RuleResultStatus.Up, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleResultStatus.Up, IsRequired: false, IsSkipped: false)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(HealthStatus.Healthy, healthInfo.State);
        Assert.Equal(2, healthInfo.Rules.Count);
        Assert.Equal(RuleResultStatus.Up, healthInfo.Rules[0].Status);
        Assert.True(healthInfo.Rules[0].IsRequired);
        Assert.Equal(RuleResultStatus.Up, healthInfo.Rules[1].Status);
        Assert.False(healthInfo.Rules[1].IsRequired);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_WithEmptyRules_IsValid()
    {
        var healthInfo = new ConfigurationHealthInfo(Array.Empty<RuleHealthInfo>());

        Assert.Equal(HealthStatus.Unknown, healthInfo.State);
        Assert.Empty(healthInfo.Rules);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsHealthyState()
    {
        var rules = new[]
        {
            new RuleHealthInfo(RuleResultStatus.Up, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleResultStatus.Skipped, IsRequired: false, IsSkipped: true)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(HealthStatus.Healthy, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.Status == RuleResultStatus.Up && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.Status == RuleResultStatus.Skipped && r.IsSkipped);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsDegradedState()
    {
        var rules = new[]
        {
            new RuleHealthInfo(RuleResultStatus.Up, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleResultStatus.Down, IsRequired: false, IsSkipped: false)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(HealthStatus.Degraded, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.Status == RuleResultStatus.Up && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.Status == RuleResultStatus.Down && !r.IsRequired);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsUnhealthyState()
    {
        var rules = new[]
        {
            new RuleHealthInfo(RuleResultStatus.Down, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleResultStatus.Unknown, IsRequired: false, IsSkipped: false)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(HealthStatus.Unhealthy, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.Status == RuleResultStatus.Down && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.Status == RuleResultStatus.Unknown);
    }

    [Theory]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    [InlineData(RuleResultStatus.Unknown)]
    [InlineData(RuleResultStatus.Up)]
    [InlineData(RuleResultStatus.Down)]
    [InlineData(RuleResultStatus.Skipped)]
    public void RuleHealthInfo_SupportsAllResultStatuses(RuleResultStatus status)
    {
        var ruleHealth = new RuleHealthInfo(status, IsRequired: true, IsSkipped: false);

        Assert.Equal(status, ruleHealth.Status);
    }
}
