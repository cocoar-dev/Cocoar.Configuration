using Cocoar.Configuration.Reactive;
using NSubstitute;

namespace Cocoar.Configuration.Flags.Tests;

/// <summary>
/// Integration tests demonstrating how FeatureFlags and Entitlements
/// work with IReactiveConfig for reactive configuration updates.
/// </summary>
public class ReactiveIntegrationTests
{
    [Fact]
    public void FeatureFlags_WithReactiveConfig_ReflectsCurrentValue()
    {
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(new BillingConfig { NewFlowEnabled = true, FlowVersion = 2 });

        var flags = new BillingFeatureFlags(config);

        Assert.True(flags.NewFlowEnabled());
        Assert.Equal(2, flags.FlowVersion());
    }

    [Fact]
    public void FeatureFlags_WhenConfigChanges_ReturnsNewValue()
    {
        var currentConfig = new BillingConfig { NewFlowEnabled = false, FlowVersion = 1 };
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var flags = new BillingFeatureFlags(config);

        Assert.False(flags.NewFlowEnabled());
        Assert.Equal(1, flags.FlowVersion());

        currentConfig = new BillingConfig { NewFlowEnabled = true, FlowVersion = 2 };

        Assert.True(flags.NewFlowEnabled());
        Assert.Equal(2, flags.FlowVersion());
    }

    [Fact]
    public void ContextualFeatureFlag_WithReactiveConfig_EvaluatesWithCurrentConfig()
    {
        var currentConfig = new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice", "bob" }
        };
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var flags = new BillingFeatureFlags(config);

        Assert.True(flags.EnabledForUser(new UserContext { Id = "alice" }));
        Assert.True(flags.EnabledForUser(new UserContext { Id = "bob" }));
        Assert.False(flags.EnabledForUser(new UserContext { Id = "charlie" }));
    }

    [Fact]
    public void ContextualFeatureFlag_WhenConfigChanges_UsesNewConfig()
    {
        var currentConfig = new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice" }
        };
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var flags = new BillingFeatureFlags(config);

        Assert.False(flags.EnabledForUser(new UserContext { Id = "charlie" }));

        currentConfig = new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice", "charlie" }
        };

        Assert.True(flags.EnabledForUser(new UserContext { Id = "charlie" }));
    }

    [Fact]
    public void Entitlements_WithReactiveConfig_ReflectsCurrentValue()
    {
        var config = Substitute.For<IReactiveConfig<PlanConfig>>();
        config.CurrentValue.Returns(new PlanConfig { Tier = "premium", UserLimit = 100 });

        var entitlements = new PlanEntitlements(config);

        Assert.True(entitlements.CanExport());
        Assert.Equal(100, entitlements.MaxUsers());
    }

    [Fact]
    public void Entitlements_WhenPlanDowngrades_ReflectsNewRestrictions()
    {
        var currentConfig = new PlanConfig { Tier = "premium", UserLimit = 100 };
        var config = Substitute.For<IReactiveConfig<PlanConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var entitlements = new PlanEntitlements(config);

        Assert.True(entitlements.CanExport());

        currentConfig = new PlanConfig { Tier = "free", UserLimit = 5 };

        Assert.False(entitlements.CanExport());
        Assert.Equal(5, entitlements.MaxUsers());
    }

    [Fact]
    public void ContextualEntitlement_EvaluatesWithCurrentConfig()
    {
        var currentConfig = new PlanConfig
        {
            EnabledFeatures = new List<string> { "export", "reports" }
        };
        var config = Substitute.For<IReactiveConfig<PlanConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var entitlements = new PlanEntitlements(config);

        Assert.True(entitlements.HasFeature(new TenantContext { Feature = "export" }));
        Assert.True(entitlements.HasFeature(new TenantContext { Feature = "reports" }));
        Assert.False(entitlements.HasFeature(new TenantContext { Feature = "api-access" }));
    }

    [Fact]
    public void FeatureFlags_CanUseWithEntitlements_ForCompositeCheck()
    {
        var billingConfig = Substitute.For<IReactiveConfig<BillingConfig>>();
        billingConfig.CurrentValue.Returns(new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice" }
        });

        var planConfig = Substitute.For<IReactiveConfig<PlanConfig>>();
        planConfig.CurrentValue.Returns(new PlanConfig { Tier = "premium" });

        var features = new BillingFeatureFlags(billingConfig);
        var entitlements = new PlanEntitlements(planConfig);

        var user = new UserContext { Id = "alice" };
        bool canUseNewBillingFlow = features.NewFlowEnabled() &&
                                    features.EnabledForUser(user) &&
                                    entitlements.CanExport();

        Assert.True(canUseNewBillingFlow);
    }

    [Fact]
    [Trait("Type", "Concurrency")]
    public async Task FeatureFlags_ConcurrentRecomputeAndEvaluation_DoesNotCorrupt()
    {
        var currentConfig = new BillingConfig { NewFlowEnabled = true, FlowVersion = 1 };
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var flags = new BillingFeatureFlags(config);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Writer: simulates reactive config recompute
        var writer = Task.Run(() =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                currentConfig = new BillingConfig
                {
                    NewFlowEnabled = i % 2 == 0,
                    FlowVersion = i,
                    BetaUsers = new List<string> { $"user_{i}" }
                };
                i++;
            }
        });

        // Reader: simulates concurrent flag evaluation
        var reader = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    _ = flags.NewFlowEnabled();
                    _ = flags.FlowVersion();
                    _ = flags.EnabledForUser(new UserContext { Id = "alice" });
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(writer, reader);
        Assert.Empty(exceptions);
    }

    [Fact]
    public void FeatureFlags_WhenCurrentValueIsNull_ThrowsNullReferenceException()
    {
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns((BillingConfig)null!);

        var flags = new BillingFeatureFlags(config);

        // Accessing a property of null CurrentValue throws NRE —
        // this documents the expected behavior before first recompute completes
        Assert.Throws<NullReferenceException>(() => flags.NewFlowEnabled());
    }

    #region Test Classes

    public class BillingConfig
    {
        public bool NewFlowEnabled { get; init; }
        public int FlowVersion { get; init; }
        public List<string> BetaUsers { get; init; } = new();
    }

    public class PlanConfig
    {
        public string Tier { get; init; } = "free";
        public int UserLimit { get; init; }
        public List<string> EnabledFeatures { get; init; } = new();
    }

    public class UserContext
    {
        public string Id { get; init; } = string.Empty;
    }

    public class TenantContext
    {
        public string Feature { get; init; } = string.Empty;
    }

    public class BillingFeatureFlags
    {
        public DateTimeOffset ExpiresAt => new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

        public FeatureFlag<bool> NewFlowEnabled { get; }
        public FeatureFlag<int> FlowVersion { get; }
        public FeatureFlag<UserContext, bool> EnabledForUser { get; }

        public BillingFeatureFlags(IReactiveConfig<BillingConfig> config)
        {
            NewFlowEnabled = () => config.CurrentValue.NewFlowEnabled;
            FlowVersion = () => config.CurrentValue.FlowVersion;
            EnabledForUser = user => config.CurrentValue.NewFlowEnabled &&
                                     config.CurrentValue.BetaUsers.Contains(user.Id);
        }
    }

    public class PlanEntitlements
    {
        public Entitlement<bool> CanExport { get; }
        public Entitlement<int> MaxUsers { get; }
        public Entitlement<TenantContext, bool> HasFeature { get; }

        public PlanEntitlements(IReactiveConfig<PlanConfig> config)
        {
            CanExport = () => config.CurrentValue.Tier != "free";
            MaxUsers = () => config.CurrentValue.UserLimit;
            HasFeature = ctx => config.CurrentValue.EnabledFeatures.Contains(ctx.Feature);
        }
    }

    #endregion
}
