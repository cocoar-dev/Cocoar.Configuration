using Cocoar.Configuration.Reactive;
using NSubstitute;

namespace Cocoar.Configuration.Flags.Tests;

/// <summary>
/// Tests for IFeatureFlags&lt;TConfig&gt; and IEntitlements&lt;TConfig&gt; source-generated partial classes.
/// The generator produces the constructor, _reactive field, and Config property.
/// </summary>
public partial class GeneratedFlagsTests
{
    // ──────────────────────────────────────────────
    // IFeatureFlags<TConfig> — compilation & basics
    // ──────────────────────────────────────────────

    [Fact]
    public void GeneratedFlags_Compiles_AndHasExpiresAt()
    {
        var reactive = Substitute.For<IReactiveConfig<TestFlagConfig>>();
        reactive.CurrentValue.Returns(new TestFlagConfig());

        var flags = new TestGeneratedFlags(reactive);

        Assert.NotNull(flags);
        Assert.Equal(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), flags.ExpiresAt);
    }

    [Fact]
    public void GeneratedFlags_Config_ReturnsCurrentValueFromReactiveConfig()
    {
        var config = new TestFlagConfig { Enabled = true, MaxItems = 42 };
        var reactive = Substitute.For<IReactiveConfig<TestFlagConfig>>();
        reactive.CurrentValue.Returns(config);

        var flags = new TestGeneratedFlags(reactive);

        Assert.True(flags.IsEnabled());
        Assert.Equal(42, flags.MaxItems());
    }

    [Fact]
    public void GeneratedFlags_ReflectsConfigChanges()
    {
        var currentConfig = new TestFlagConfig { Enabled = false, MaxItems = 5 };
        var reactive = Substitute.For<IReactiveConfig<TestFlagConfig>>();
        reactive.CurrentValue.Returns(_ => currentConfig);

        var flags = new TestGeneratedFlags(reactive);

        Assert.False(flags.IsEnabled());
        Assert.Equal(5, flags.MaxItems());

        currentConfig = new TestFlagConfig { Enabled = true, MaxItems = 99 };

        Assert.True(flags.IsEnabled());
        Assert.Equal(99, flags.MaxItems());
    }

    [Fact]
    public void GeneratedFlags_ExpiresAt_ReturnsUserDefinedValue()
    {
        var reactive = Substitute.For<IReactiveConfig<TestFlagConfig>>();
        reactive.CurrentValue.Returns(new TestFlagConfig());

        var flags = new TestGeneratedFlags(reactive);

        Assert.Equal(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), flags.ExpiresAt);
        Assert.False(flags.IsExpired);
    }

    // ──────────────────────────────────────────────
    // IEntitlements<TConfig> — compilation & basics
    // ──────────────────────────────────────────────

    [Fact]
    public void GeneratedEntitlements_Compiles_Successfully()
    {
        var reactive = Substitute.For<IReactiveConfig<TestEntitlementConfig>>();
        reactive.CurrentValue.Returns(new TestEntitlementConfig());

        var entitlements = new TestGeneratedEntitlements(reactive);

        Assert.NotNull(entitlements);
    }

    [Fact]
    public void GeneratedEntitlements_Config_ReturnsCurrentValueFromReactiveConfig()
    {
        var config = new TestEntitlementConfig { CanExport = true, MaxUsers = 50 };
        var reactive = Substitute.For<IReactiveConfig<TestEntitlementConfig>>();
        reactive.CurrentValue.Returns(config);

        var entitlements = new TestGeneratedEntitlements(reactive);

        Assert.True(entitlements.CanExport());
        Assert.Equal(50, entitlements.MaxUsers());
    }

    [Fact]
    public void GeneratedEntitlements_ReflectsConfigChanges()
    {
        var currentConfig = new TestEntitlementConfig { CanExport = true, MaxUsers = 100 };
        var reactive = Substitute.For<IReactiveConfig<TestEntitlementConfig>>();
        reactive.CurrentValue.Returns(_ => currentConfig);

        var entitlements = new TestGeneratedEntitlements(reactive);

        Assert.True(entitlements.CanExport());
        Assert.Equal(100, entitlements.MaxUsers());

        currentConfig = new TestEntitlementConfig { CanExport = false, MaxUsers = 5 };

        Assert.False(entitlements.CanExport());
        Assert.Equal(5, entitlements.MaxUsers());
    }

    // ──────────────────────────────────────────────
    // Old pattern still works (regression)
    // ──────────────────────────────────────────────

    [Fact]
    public void OldPattern_ManualConstructor_StillWorks()
    {
        var reactive = Substitute.For<IReactiveConfig<TestFlagConfig>>();
        reactive.CurrentValue.Returns(new TestFlagConfig { Enabled = true, MaxItems = 7 });

        var flags = new OldStyleFlags(reactive);

        Assert.True(flags.IsEnabled());
        Assert.Equal(7, flags.MaxItems());
    }

    // ──────────────────────────────────────────────
    // Test classes: IFeatureFlags<T>
    // ──────────────────────────────────────────────

    public class TestFlagConfig
    {
        public bool Enabled { get; set; } = true;
        public int MaxItems { get; set; } = 10;
    }

    public partial class TestGeneratedFlags : IFeatureFlags<TestFlagConfig>
    {
        public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> IsEnabled => () => Config.Enabled;
        public FeatureFlag<int> MaxItems => () => Config.MaxItems;
    }

    // ──────────────────────────────────────────────
    // Test classes: IEntitlements<T>
    // ──────────────────────────────────────────────

    public class TestEntitlementConfig
    {
        public bool CanExport { get; set; } = true;
        public int MaxUsers { get; set; } = 10;
    }

    public partial class TestGeneratedEntitlements : IEntitlements<TestEntitlementConfig>
    {
        public Entitlement<bool> CanExport => () => Config.CanExport;
        public Entitlement<int> MaxUsers => () => Config.MaxUsers;
    }

    // ──────────────────────────────────────────────
    // Test classes: old pattern (regression)
    // ──────────────────────────────────────────────

    public class OldStyleFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

        public FeatureFlag<bool> IsEnabled { get; }
        public FeatureFlag<int> MaxItems { get; }

        public OldStyleFlags(IReactiveConfig<TestFlagConfig> config)
        {
            IsEnabled = () => config.CurrentValue.Enabled;
            MaxItems = () => config.CurrentValue.MaxItems;
        }
    }
}
