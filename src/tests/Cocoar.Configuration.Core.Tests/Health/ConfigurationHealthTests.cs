
using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Health;

public class ConfigurationHealthTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthStatus_HasExpectedValues()
    {

        Assert.Equal(0, (int)ConfigurationHealthStatus.Healthy);
        Assert.Equal(1, (int)ConfigurationHealthStatus.Degraded);
        Assert.Equal(2, (int)ConfigurationHealthStatus.Unhealthy);
        Assert.Equal(3, (int)ConfigurationHealthStatus.Unknown);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void RuleEvaluationState_HasExpectedValues()
    {

        Assert.Equal(0, (int)RuleEvaluationState.NotEvaluated);
        Assert.Equal(1, (int)RuleEvaluationState.Skipped);
        Assert.Equal(2, (int)RuleEvaluationState.Evaluating);
        Assert.Equal(3, (int)RuleEvaluationState.Success);
        Assert.Equal(4, (int)RuleEvaluationState.Failed);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void RuleHealthInfo_CanBeCreated()
    {
        var ruleHealth = new RuleHealthInfo(
            RuleEvaluationState.Success,
            IsRequired: true,
            IsSkipped: false
        );

        Assert.Equal(RuleEvaluationState.Success, ruleHealth.State);
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
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: false, IsSkipped: false)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(ConfigurationHealthStatus.Healthy, healthInfo.State);
        Assert.Equal(2, healthInfo.Rules.Count);
        Assert.Equal(RuleEvaluationState.Success, healthInfo.Rules[0].State);
        Assert.True(healthInfo.Rules[0].IsRequired);
        Assert.Equal(RuleEvaluationState.Success, healthInfo.Rules[1].State);
        Assert.False(healthInfo.Rules[1].IsRequired);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_WithEmptyRules_IsValid()
    {
        var healthInfo = new ConfigurationHealthInfo(Array.Empty<RuleHealthInfo>());

        Assert.Equal(ConfigurationHealthStatus.Unknown, healthInfo.State);
        Assert.Empty(healthInfo.Rules);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsHealthyState()
    {
        var rules = new[]
        {
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.Skipped, IsRequired: false, IsSkipped: true)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(ConfigurationHealthStatus.Healthy, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Success && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Skipped && r.IsSkipped);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsDegradedState()
    {
        var rules = new[]
        {
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.Failed, IsRequired: false, IsSkipped: false)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(ConfigurationHealthStatus.Degraded, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Success && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Failed && !r.IsRequired);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsUnhealthyState()
    {
        var rules = new[]
        {
            new RuleHealthInfo(RuleEvaluationState.Failed, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.NotEvaluated, IsRequired: false, IsSkipped: false)
        };

        var healthInfo = new ConfigurationHealthInfo(rules);

        Assert.Equal(ConfigurationHealthStatus.Unhealthy, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Failed && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.NotEvaluated);
    }

    [Theory]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    [InlineData(RuleEvaluationState.NotEvaluated)]
    [InlineData(RuleEvaluationState.Skipped)]
    [InlineData(RuleEvaluationState.Evaluating)]
    [InlineData(RuleEvaluationState.Success)]
    [InlineData(RuleEvaluationState.Failed)]
    public void RuleHealthInfo_SupportsAllEvaluationStates(RuleEvaluationState state)
    {
        var ruleHealth = new RuleHealthInfo(state, IsRequired: true, IsSkipped: false);

        Assert.Equal(state, ruleHealth.State);
    }
}



