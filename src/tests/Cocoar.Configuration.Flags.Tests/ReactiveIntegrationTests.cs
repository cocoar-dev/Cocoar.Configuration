using Cocoar.Configuration.Reactive;
using NSubstitute;

namespace Cocoar.Configuration.Flags.Tests;

/// <summary>
/// Integration tests demonstrating how FeatureFlags and Entitlements
/// work with IReactiveConfig for reactive configuration updates.
/// </summary>
public class ReactiveIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    [Fact]
    public void FeatureFlags_WithReactiveConfig_ReflectsCurrentValue()
    {
        // Arrange
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(new BillingConfig { NewFlowEnabled = true, FlowVersion = 2 });

        var flags = Track(new BillingFeatureFlags(config));

        // Act & Assert - call delegates
        Assert.True(flags.NewFlowEnabled());
        Assert.Equal(2, flags.FlowVersion());
    }

    [Fact]
    public void FeatureFlags_WhenConfigChanges_ReturnsNewValue()
    {
        // Arrange
        var currentConfig = new BillingConfig { NewFlowEnabled = false, FlowVersion = 1 };
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var flags = Track(new BillingFeatureFlags(config));

        // Act - Initial state
        Assert.False(flags.NewFlowEnabled());
        Assert.Equal(1, flags.FlowVersion());

        // Act - Update config (simulating reactive update)
        currentConfig = new BillingConfig { NewFlowEnabled = true, FlowVersion = 2 };

        // Assert - Flags reflect new value (delegates re-evaluate)
        Assert.True(flags.NewFlowEnabled());
        Assert.Equal(2, flags.FlowVersion());
    }

    [Fact]
    public void ContextualFeatureFlag_WithReactiveConfig_EvaluatesWithCurrentConfig()
    {
        // Arrange
        var currentConfig = new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice", "bob" }
        };
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var flags = Track(new BillingFeatureFlags(config));

        // Act & Assert - contextual flag takes context parameter
        Assert.True(flags.EnabledForUser(new UserContext { Id = "alice" }));
        Assert.True(flags.EnabledForUser(new UserContext { Id = "bob" }));
        Assert.False(flags.EnabledForUser(new UserContext { Id = "charlie" }));
    }

    [Fact]
    public void ContextualFeatureFlag_WhenConfigChanges_UsesNewConfig()
    {
        // Arrange
        var currentConfig = new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice" }
        };
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var flags = Track(new BillingFeatureFlags(config));

        // Initial - charlie is not a beta user
        Assert.False(flags.EnabledForUser(new UserContext { Id = "charlie" }));

        // Update config to add charlie to beta users
        currentConfig = new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice", "charlie" }
        };

        // Now charlie is a beta user
        Assert.True(flags.EnabledForUser(new UserContext { Id = "charlie" }));
    }

    [Fact]
    public void Entitlements_WithReactiveConfig_ReflectsCurrentValue()
    {
        // Arrange
        var config = Substitute.For<IReactiveConfig<PlanConfig>>();
        config.CurrentValue.Returns(new PlanConfig { Tier = "premium", UserLimit = 100 });

        var entitlements = Track(new PlanEntitlements(config));

        // Act & Assert - call delegates
        Assert.True(entitlements.CanExport());
        Assert.Equal(100, entitlements.MaxUsers());
    }

    [Fact]
    public void Entitlements_WhenPlanDowngrades_ReflectsNewRestrictions()
    {
        // Arrange
        var currentConfig = new PlanConfig { Tier = "premium", UserLimit = 100 };
        var config = Substitute.For<IReactiveConfig<PlanConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var entitlements = Track(new PlanEntitlements(config));

        // Premium can export
        Assert.True(entitlements.CanExport());

        // Downgrade to free
        currentConfig = new PlanConfig { Tier = "free", UserLimit = 5 };

        // Free cannot export
        Assert.False(entitlements.CanExport());
        Assert.Equal(5, entitlements.MaxUsers());
    }

    [Fact]
    public void ContextualEntitlement_EvaluatesWithCurrentConfig()
    {
        // Arrange
        var currentConfig = new PlanConfig
        {
            EnabledFeatures = new List<string> { "export", "reports" }
        };
        var config = Substitute.For<IReactiveConfig<PlanConfig>>();
        config.CurrentValue.Returns(_ => currentConfig);

        var entitlements = Track(new PlanEntitlements(config));

        // Act & Assert - contextual entitlement takes context parameter
        Assert.True(entitlements.HasFeature(new TenantContext { Feature = "export" }));
        Assert.True(entitlements.HasFeature(new TenantContext { Feature = "reports" }));
        Assert.False(entitlements.HasFeature(new TenantContext { Feature = "api-access" }));
    }

    [Fact]
    public void FeatureFlags_CanUseWithEntitlements_ForCompositeCheck()
    {
        // Arrange
        var billingConfig = Substitute.For<IReactiveConfig<BillingConfig>>();
        billingConfig.CurrentValue.Returns(new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = new List<string> { "alice" }
        });

        var planConfig = Substitute.For<IReactiveConfig<PlanConfig>>();
        planConfig.CurrentValue.Returns(new PlanConfig { Tier = "premium" });

        var features = Track(new BillingFeatureFlags(billingConfig));
        var entitlements = Track(new PlanEntitlements(planConfig));

        // Act - Composition: Feature enabled AND entitled
        var user = new UserContext { Id = "alice" };
        bool canUseNewBillingFlow = features.NewFlowEnabled() &&
                                    features.EnabledForUser(user) &&
                                    entitlements.CanExport();

        // Assert
        Assert.True(canUseNewBillingFlow);
    }

    [Fact]
    public void FeatureFlags_GetMetadata_ReturnsCorrectMetadata()
    {
        // Arrange
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(new BillingConfig());

        var flags = Track(new BillingFeatureFlags(config));

        // Act
        var metadata = flags.GetMetadata(flags.NewFlowEnabled);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal("NewFlowEnabled", metadata.Name);
        Assert.Equal("Enables new billing flow", metadata.Description);
    }

    [Fact]
    public void FeatureFlags_GetMetadata_PerFlagExpiration_OverridesClassExpiration()
    {
        // Arrange
        var config = Substitute.For<IReactiveConfig<BillingConfig>>();
        config.CurrentValue.Returns(new BillingConfig());

        var flags = Track(new BillingFeatureFlags(config));

        // Act
        var newFlowMeta = flags.GetMetadata(flags.NewFlowEnabled);
        var flowVersionMeta = flags.GetMetadata(flags.FlowVersion);

        // Assert - NewFlowEnabled has custom expiry
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero), newFlowMeta!.ExpiresAt);

        // Assert - FlowVersion uses class-level expiry
        Assert.Equal(flags.ExpiresAt, flowVersionMeta!.ExpiresAt);
    }

    [Fact]
    public void Entitlements_GetMetadata_ReturnsCorrectMetadata()
    {
        // Arrange
        var config = Substitute.For<IReactiveConfig<PlanConfig>>();
        config.CurrentValue.Returns(new PlanConfig());

        var entitlements = Track(new PlanEntitlements(config));

        // Act
        var metadata = entitlements.GetMetadata(entitlements.CanExport);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal("CanExport", metadata.Name);
        Assert.Equal("Can export data", metadata.Description);
    }

    private T Track<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            d.Dispose();
        }
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

    public class BillingFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly IReactiveConfig<BillingConfig> _config;

        // Flags as delegates - evaluated on each call, always fresh
        public Flag<bool> NewFlowEnabled { get; }
        public Flag<int> FlowVersion { get; }
        public Flag<UserContext, bool> EnabledForUser { get; }

        public BillingFeatureFlags(IReactiveConfig<BillingConfig> config)
        {
            _config = config;

            NewFlowEnabled = DefineFlag(
                nameof(NewFlowEnabled),
                () => _config.CurrentValue.NewFlowEnabled,
                expiresAt: new(2025, 3, 1, 0, 0, 0, TimeSpan.Zero),
                description: "Enables new billing flow"
            );

            FlowVersion = DefineFlag(
                nameof(FlowVersion),
                () => _config.CurrentValue.FlowVersion,
                description: "Current billing flow version"
            );

            EnabledForUser = DefineFlag<UserContext, bool>(
                nameof(EnabledForUser),
                user => _config.CurrentValue.NewFlowEnabled &&
                        _config.CurrentValue.BetaUsers.Contains(user.Id),
                description: "Per-user beta access"
            );
        }
    }

    public class PlanEntitlements : Entitlements
    {
        private readonly IReactiveConfig<PlanConfig> _config;

        // Entitlements as delegates
        public Entitlement<bool> CanExport { get; }
        public Entitlement<int> MaxUsers { get; }
        public Entitlement<TenantContext, bool> HasFeature { get; }

        public PlanEntitlements(IReactiveConfig<PlanConfig> config)
        {
            _config = config;

            CanExport = DefineEntitlement(
                nameof(CanExport),
                () => _config.CurrentValue.Tier != "free",
                description: "Can export data"
            );

            MaxUsers = DefineEntitlement(
                nameof(MaxUsers),
                () => _config.CurrentValue.UserLimit,
                description: "Maximum allowed users"
            );

            HasFeature = DefineEntitlement<TenantContext, bool>(
                nameof(HasFeature),
                ctx => _config.CurrentValue.EnabledFeatures.Contains(ctx.Feature),
                description: "Check if tenant has specific feature"
            );
        }
    }

    #endregion
}
