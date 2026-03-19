using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags.Internal;
using Cocoar.Configuration.Health;

namespace Cocoar.Configuration.Flags.Tests;

/// <summary>
/// Integration tests for UseFeatureFlags / UseEntitlements builder extensions
/// and health source wiring.
/// </summary>
public class BuilderIntegrationTests
{
    // ──────────────────────────────────────────────
    // FeatureFlagsHealthSource
    // ──────────────────────────────────────────────

    [Fact]
    public void FeatureFlagsHealthSource_WithExpiredDescriptor_ReportsExpired()
    {
        var descriptors = new FeatureFlagsDescriptors([new FeatureFlagClassDescriptor(
            Type: typeof(ExpiredTestFlags),
            ExpiresAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Flags: [new FlagDefinitionDescriptor("OldFeature", null)])]);

        var source = new FeatureFlagsHealthSource(descriptors);
        Assert.True(source.HasExpiredFlags());
    }

    [Fact]
    public void FeatureFlagsHealthSource_WithNoExpiredDescriptors_ReportsNotExpired()
    {
        var descriptors = new FeatureFlagsDescriptors([new FeatureFlagClassDescriptor(
            Type: typeof(FutureTestFlags),
            ExpiresAt: new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero),
            Flags: [new FlagDefinitionDescriptor("NewFeature", null)])]);

        var source = new FeatureFlagsHealthSource(descriptors);
        Assert.False(source.HasExpiredFlags());
    }

    [Fact]
    public void FeatureFlagsHealthSource_WithMixedDescriptors_ReportsExpired()
    {
        var descriptors = new FeatureFlagsDescriptors([
            new FeatureFlagClassDescriptor(
                Type: typeof(ExpiredTestFlags),
                ExpiresAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Flags: [new FlagDefinitionDescriptor("OldFeature", null)]),
            new FeatureFlagClassDescriptor(
                Type: typeof(FutureTestFlags),
                ExpiresAt: new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero),
                Flags: [new FlagDefinitionDescriptor("NewFeature", null)])]);

        var source = new FeatureFlagsHealthSource(descriptors);
        Assert.True(source.HasExpiredFlags());
    }

    // ──────────────────────────────────────────────
    // UseFeatureFlags / UseEntitlements builder wiring
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UseFeatureFlags_ExpiredFlags_HealthIsDegraded()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<ExpiredTestFlags>()]));

        manager.ScheduleRecompute(0);
        if (manager.CurrentRecomputeTask is { } task) await task;

        Assert.Equal(HealthStatus.Degraded, manager.HealthStatus);
        Assert.False(manager.IsHealthy);
    }

    [Fact]
    public async Task UseFeatureFlags_WithNoExpiredFlags_HealthIsNotDegraded()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<FutureTestFlags>()]));

        manager.ScheduleRecompute(0);
        if (manager.CurrentRecomputeTask is { } task) await task;

        // No rules and no expired flags => Unknown (no rules configured)
        Assert.NotEqual(HealthStatus.Degraded, manager.HealthStatus);
    }

    [Fact]
    public void UseEntitlements_WithoutUseFeatureFlags_DoesNotThrow()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => [e.Register<SimpleTestEntitlements>()]));

        // No rules, no flags => Unknown
        Assert.Equal(HealthStatus.Unknown, manager.HealthStatus);
    }

    // ──────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────

    internal sealed class ExpiredTestFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> OldFeature { get; }

        public ExpiredTestFlags()
        {
            OldFeature = () => true;
        }
    }

    internal sealed class FutureTestFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> NewFeature { get; }

        public FutureTestFlags()
        {
            NewFeature = () => true;
        }
    }

    internal sealed class SimpleTestEntitlements : Entitlements { }
}
