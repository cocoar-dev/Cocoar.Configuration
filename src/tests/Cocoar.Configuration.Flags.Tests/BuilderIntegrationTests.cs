using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags.Internal;
using Cocoar.Configuration.Health;

namespace Cocoar.Configuration.Flags.Tests;

/// <summary>
/// Integration tests for UseFeatureFlags / UseEntitlements builder extensions,
/// health source wiring, and ConfigHealthSnapshot expired flags behaviour.
/// </summary>
public class BuilderIntegrationTests
{
    // ──────────────────────────────────────────────
    // ConfigHealthSnapshot — expired flags affect OverallStatus
    // ──────────────────────────────────────────────

    [Fact]
    public void ConfigHealthSnapshot_WithExpiredFlags_IsStatusDegraded()
    {
        var entry = new FlagClassHealthEntry("OldFlags", DateTimeOffset.UtcNow.AddYears(-1), true, 2, 1);
        var snapshot = new ConfigHealthSnapshot(1, DateTime.UtcNow, 1, [], [entry]);

        Assert.Equal(HealthStatus.Degraded, snapshot.OverallStatus);
        Assert.Single(snapshot.ExpiredFeatureFlags);
    }

    [Fact]
    public void ConfigHealthSnapshot_WithNoExpiredFlags_PreservesOriginalStatus()
    {
        // No rules, no expired flags → Unknown (pre-existing behaviour: empty rules = Unknown)
        var snapshotNoRules = new ConfigHealthSnapshot(1, DateTime.UtcNow, 1, []);
        Assert.Equal(HealthStatus.Unknown, snapshotNoRules.OverallStatus);
        Assert.Empty(snapshotNoRules.ExpiredFeatureFlags);

        // Has a healthy rule, no expired flags → Healthy
        var ruleUp = new RuleHealthEntry(0, "rule", required: true, RuleResultStatus.Up,
            DateTime.UtcNow, null, 0, null, null);
        var snapshotHealthy = new ConfigHealthSnapshot(2, DateTime.UtcNow, 1, [ruleUp]);
        Assert.Equal(HealthStatus.Healthy, snapshotHealthy.OverallStatus);
    }

    [Fact]
    public void ConfigHealthSnapshot_WithExpiredFlagsAndRequiredRuleDown_IsStatusUnhealthy()
    {
        var ruleDown = new RuleHealthEntry(0, "rule", required: true, RuleResultStatus.Down,
            null, null, 1, null, null);
        var expiredEntry = new FlagClassHealthEntry("OldFlags", DateTimeOffset.UtcNow.AddYears(-1), true, 1, 1);

        var snapshot = new ConfigHealthSnapshot(1, DateTime.UtcNow, 1, [ruleDown], [expiredEntry]);

        // Unhealthy (required rule down) takes precedence over Degraded (expired flags)
        Assert.Equal(HealthStatus.Unhealthy, snapshot.OverallStatus);
    }

    // ──────────────────────────────────────────────
    // FeatureFlagsHealthSource
    // ──────────────────────────────────────────────

    [Fact]
    public void FeatureFlagsHealthSource_WithExpiredDescriptor_ReturnsEntries()
    {
        var registry = new FeatureFlagsRegistry();
        registry.RegisterDescriptor(new FeatureFlagClassDescriptor(
            Type: typeof(ExpiredTestFlags),
            ExpiresAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Flags: [new FlagDefinitionDescriptor("OldFeature", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null)]));

        var source = new FeatureFlagsHealthSource(registry);
        var expired = source.GetExpiredFeatureFlags();

        Assert.Single(expired);
        var entry = expired[0];
        Assert.Equal(nameof(ExpiredTestFlags), entry.TypeName);
        Assert.True(entry.IsExpired);
        Assert.Equal(1, entry.TotalFlags);
        Assert.Equal(1, entry.ExpiredFlags);
    }

    [Fact]
    public void FeatureFlagsHealthSource_WithNoExpiredDescriptors_ReturnsEmpty()
    {
        var registry = new FeatureFlagsRegistry();
        registry.RegisterDescriptor(new FeatureFlagClassDescriptor(
            Type: typeof(FutureTestFlags),
            ExpiresAt: new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero),
            Flags: [new FlagDefinitionDescriptor("NewFeature", new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero), null)]));

        var source = new FeatureFlagsHealthSource(registry);

        Assert.Empty(source.GetExpiredFeatureFlags());
    }

    [Fact]
    public void FeatureFlagsHealthSource_WithMixedRegistry_ReturnsOnlyExpired()
    {
        var registry = new FeatureFlagsRegistry();
        registry.RegisterDescriptor(new FeatureFlagClassDescriptor(
            Type: typeof(ExpiredTestFlags),
            ExpiresAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Flags: [new FlagDefinitionDescriptor("OldFeature", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null)]));
        registry.RegisterDescriptor(new FeatureFlagClassDescriptor(
            Type: typeof(FutureTestFlags),
            ExpiresAt: new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero),
            Flags: [new FlagDefinitionDescriptor("NewFeature", new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero), null)]));

        var source = new FeatureFlagsHealthSource(registry);
        var result = source.GetExpiredFeatureFlags();

        Assert.Single(result);
        Assert.Equal(nameof(ExpiredTestFlags), result[0].TypeName);
    }

    // ──────────────────────────────────────────────
    // UseFeatureFlags / UseEntitlements builder wiring
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UseFeatureFlags_HealthSnapshotIncludesExpiredFlags_AfterRecompute()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f.Register<ExpiredTestFlags>()));

        manager.ScheduleRecompute(0);
        if (manager.CurrentRecomputeTask is { } task) await task;

        var snapshot = manager.GetHealthService().Snapshot;

        Assert.Single(snapshot.ExpiredFeatureFlags);
        Assert.Equal(nameof(ExpiredTestFlags), snapshot.ExpiredFeatureFlags[0].TypeName);
        Assert.Equal(HealthStatus.Degraded, snapshot.OverallStatus);
    }

    [Fact]
    public async Task UseFeatureFlags_WithNoExpiredFlags_ExpiredFlagsListIsEmpty()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f.Register<FutureTestFlags>()));

        manager.ScheduleRecompute(0);
        if (manager.CurrentRecomputeTask is { } task) await task;

        var snapshot = manager.GetHealthService().Snapshot;

        // No expired flags → not in the list
        Assert.Empty(snapshot.ExpiredFeatureFlags);
    }

    [Fact]
    public void UseEntitlements_WithoutUseFeatureFlags_DoesNotThrow()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => e.Register<SimpleTestEntitlements>()));

        // Health should be unaffected by entitlements (no expiry)
        var snapshot = manager.GetHealthService().Snapshot;
        Assert.Empty(snapshot.ExpiredFeatureFlags);
    }

    // ──────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────

    internal sealed class ExpiredTestFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> OldFeature { get; }

        public ExpiredTestFlags()
        {
            OldFeature = DefineFlag(nameof(OldFeature), () => true);
        }
    }

    internal sealed class FutureTestFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> NewFeature { get; }

        public FutureTestFlags()
        {
            NewFeature = DefineFlag(nameof(NewFeature), () => true);
        }
    }

    internal sealed class SimpleTestEntitlements : Entitlements { }
}
