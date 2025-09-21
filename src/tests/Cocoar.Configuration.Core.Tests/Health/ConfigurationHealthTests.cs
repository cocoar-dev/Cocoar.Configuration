using System;
using System.Linq;
using Cocoar.Configuration.Health;
using Xunit;

namespace Cocoar.Configuration.Core.Tests.Health;

public class ConfigurationHealthTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthStatus_HasExpectedValues()
    {
        // Arrange & Act - Verify all expected enum values exist
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
        // Arrange & Act - Verify rule evaluation state enum values exist
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
        // Act
        var ruleHealth = new RuleHealthInfo(
            RuleEvaluationState.Success,
            IsRequired: true,
            IsSkipped: false
        );

        // Assert
        Assert.Equal(RuleEvaluationState.Success, ruleHealth.State);
        Assert.True(ruleHealth.IsRequired);
        Assert.False(ruleHealth.IsSkipped);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_CanBeCreated()
    {
        // Arrange
        var rules = new[]
        {
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: false, IsSkipped: false)
        };

        // Act
        var healthInfo = new ConfigurationHealthInfo(rules);

        // Assert
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
        // Act
        var healthInfo = new ConfigurationHealthInfo(Array.Empty<RuleHealthInfo>());

        // Assert
        Assert.Equal(ConfigurationHealthStatus.Unknown, healthInfo.State);
        Assert.Empty(healthInfo.Rules);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsHealthyState()
    {
        // Arrange
        var rules = new[]
        {
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.Skipped, IsRequired: false, IsSkipped: true)
        };

        // Act
        var healthInfo = new ConfigurationHealthInfo(rules);

        // Assert
        Assert.Equal(ConfigurationHealthStatus.Healthy, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Success && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Skipped && r.IsSkipped);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsDegradedState()
    {
        // Arrange
        var rules = new[]
        {
            new RuleHealthInfo(RuleEvaluationState.Success, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.Failed, IsRequired: false, IsSkipped: false)
        };

        // Act
        var healthInfo = new ConfigurationHealthInfo(rules);

        // Assert
        Assert.Equal(ConfigurationHealthStatus.Degraded, healthInfo.State);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Success && r.IsRequired);
        Assert.Contains(healthInfo.Rules, r => r.State == RuleEvaluationState.Failed && !r.IsRequired);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Area", "Health")]
    public void ConfigurationHealthInfo_SupportsUnhealthyState()
    {
        // Arrange
        var rules = new[]
        {
            new RuleHealthInfo(RuleEvaluationState.Failed, IsRequired: true, IsSkipped: false),
            new RuleHealthInfo(RuleEvaluationState.NotEvaluated, IsRequired: false, IsSkipped: false)
        };

        // Act
        var healthInfo = new ConfigurationHealthInfo(rules);

        // Assert
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
        // Act
        var ruleHealth = new RuleHealthInfo(state, IsRequired: true, IsSkipped: false);

        // Assert
        Assert.Equal(state, ruleHealth.State);
    }
}