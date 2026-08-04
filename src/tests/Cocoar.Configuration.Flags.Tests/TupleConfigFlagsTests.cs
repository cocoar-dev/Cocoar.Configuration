using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Reactive;
using NSubstitute;

namespace Cocoar.Configuration.Flags.Tests;

public class FeatureConfig
{
    public bool NewCheckoutEnabled { get; set; }
}

public class TenantConfig
{
    public bool AllowExperiments { get; set; }
}

/// <summary>
/// The "Multiple Config Sources" pattern from guide/flags/defining-flags.md, using a named value tuple. The
/// interface's own typeparam doc promises "the configuration type (or value tuple of types)".
/// </summary>
public partial class RolloutFlags : IFeatureFlags<(FeatureConfig Features, TenantConfig Tenant)>
{
    public DateTimeOffset ExpiresAt => new(2099, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public bool NewCheckout() => Config.Features.NewCheckoutEnabled && Config.Tenant.AllowExperiments;
}

/// <summary>Same for entitlements, which carry the identical typeparam promise.</summary>
public partial class TenantEntitlements : IEntitlements<(FeatureConfig Features, TenantConfig Tenant)>
{
    public bool MayExperiment() => Config.Tenant.AllowExperiments;
}

public class TupleConfigFlagsTests
{
    [Fact]
    public void FlagClass_WithTupleConfig_ReadsBothConfigs()
    {
        var reactive = Substitute.For<IReactiveConfig<(FeatureConfig Features, TenantConfig Tenant)>>();
        reactive.CurrentValue.Returns((
            new FeatureConfig { NewCheckoutEnabled = true },
            new TenantConfig { AllowExperiments = true }));

        var flags = new RolloutFlags(reactive);

        Assert.True(flags.NewCheckout());
        Assert.Equal(new DateTimeOffset(2099, 9, 1, 0, 0, 0, TimeSpan.Zero), flags.ExpiresAt);
    }

    [Fact]
    public void FlagClass_WithTupleConfig_RequiresBothConfigs()
    {
        var reactive = Substitute.For<IReactiveConfig<(FeatureConfig Features, TenantConfig Tenant)>>();
        reactive.CurrentValue.Returns((
            new FeatureConfig { NewCheckoutEnabled = true },
            new TenantConfig { AllowExperiments = false }));

        Assert.False(new RolloutFlags(reactive).NewCheckout());
    }

    /// <summary>
    /// The path that actually matters: registration via <c>UseFeatureFlags</c> and resolution through
    /// <c>GetFeatureFlags&lt;T&gt;()</c>, which reflects over the generated constructor and calls
    /// <c>ConfigManager.GetReactiveConfig&lt;TConfig&gt;()</c> with the tuple type. Constructing the class
    /// directly (as the tests above do) would not exercise that.
    /// </summary>
    [Fact]
    public void FlagClass_WithTupleConfig_ResolvesThroughTheConfigManager()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<FeatureConfig>().FromStaticJson("""{"NewCheckoutEnabled":true}"""),
                rules.For<TenantConfig>().FromStaticJson("""{"AllowExperiments":true}"""),
            ])
            .UseFeatureFlags(flags => [flags.Register<RolloutFlags>()]));

        var flags = manager.GetFeatureFlags<RolloutFlags>();

        Assert.True(flags.NewCheckout());
    }

    /// <summary>Same for the entitlement path, which resolves through its own setup and cache.</summary>
    [Fact]
    public void EntitlementClass_WithTupleConfig_ResolvesThroughTheConfigManager()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<FeatureConfig>().FromStaticJson("""{"NewCheckoutEnabled":false}"""),
                rules.For<TenantConfig>().FromStaticJson("""{"AllowExperiments":true}"""),
            ])
            .UseEntitlements(e => [e.Register<TenantEntitlements>()]));

        Assert.True(manager.GetEntitlements<TenantEntitlements>().MayExperiment());
    }

    [Fact]
    public void EntitlementClass_WithTupleConfig_ReadsItsConfig()
    {
        var reactive = Substitute.For<IReactiveConfig<(FeatureConfig Features, TenantConfig Tenant)>>();
        reactive.CurrentValue.Returns((
            new FeatureConfig(),
            new TenantConfig { AllowExperiments = true }));

        Assert.True(new TenantEntitlements(reactive).MayExperiment());
    }
}
