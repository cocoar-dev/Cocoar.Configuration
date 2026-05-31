using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Providers; // FromStaticJson / FromStatic

namespace Cocoar.Configuration.MultiTenant.Tests;

public sealed record BillingConfig { public bool PremiumBilling { get; init; } }

// Source-generated: the generator emits the ctor taking IReactiveConfig<BillingConfig> + the Config property.
public partial class BillingFlags : IFeatureFlags<BillingConfig>
{
    public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public FeatureFlag<bool> PremiumEnabled => () => Config.PremiumBilling;
}

public sealed record PlanConfig
{
    public string Plan { get; init; } = "free";
    public int MaxSeats { get; init; }
}

public partial class PlanEntitlements : IEntitlements<PlanConfig>
{
    public Entitlement<bool> CanExport => () => Config.Plan == "enterprise";
    public Entitlement<int> MaxSeats => () => Config.MaxSeats;
}

/// <summary>
/// (P3) per-tenant flags/entitlements through the real API: the SAME source-generated class constructed with
/// each tenant's own IReactiveConfig&lt;T&gt; (no generator change). Ports the POC's TenantFlagsPocTests.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantFlagsTests
{
    [Fact]
    public async Task FeatureFlag_EvaluatesPerTenant()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<BillingConfig>().FromStaticJson("""{ "PremiumBilling": false }"""),
                rules.For<BillingConfig>().FromStatic(a => new BillingConfig { PremiumBilling = a.Tenant == "A" }).TenantScoped(),
            ])
            .UseFeatureFlags(flags => [flags.Register<BillingFlags>()])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("A");
        await tenants.InitializeTenantAsync("B");

        var flagsA = mgr.GetFeatureFlagsForTenant<BillingFlags>("A");
        var flagsB = mgr.GetFeatureFlagsForTenant<BillingFlags>("B");

        Assert.True(flagsA.PremiumEnabled());   // tenant A: premium on
        Assert.False(flagsB.PremiumEnabled());  // tenant B: default off (inherited base)
        Assert.False(mgr.GetFeatureFlags<BillingFlags>().PremiumEnabled()); // global: overlay skipped -> base

        // Per-(tenant, T) singleton caching; tenants never alias each other.
        Assert.Same(flagsA, mgr.GetFeatureFlagsForTenant<BillingFlags>("A"));
        Assert.NotSame(flagsA, flagsB);
    }

    [Fact]
    public async Task Entitlement_EvaluatesPerTenant()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<PlanConfig>().FromStaticJson("""{ "Plan": "free", "MaxSeats": 5 }"""),
                rules.For<PlanConfig>().FromStatic(a => a.Tenant == "A"
                    ? new PlanConfig { Plan = "enterprise", MaxSeats = 500 }
                    : new PlanConfig { Plan = "free", MaxSeats = 5 }).TenantScoped(),
            ])
            .UseEntitlements(e => [e.Register<PlanEntitlements>()])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("A");
        await tenants.InitializeTenantAsync("B");

        var entA = mgr.GetEntitlementsForTenant<PlanEntitlements>("A");
        var entB = mgr.GetEntitlementsForTenant<PlanEntitlements>("B");

        Assert.True(entA.CanExport());    // enterprise
        Assert.False(entB.CanExport());   // free (inherited base)
        Assert.Equal(500, entA.MaxSeats());
        Assert.Equal(5, entB.MaxSeats());
    }

    [Fact]
    public void GetFeatureFlagsForTenant_BeforeInit_Throws()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<BillingConfig>().FromStaticJson("""{ "PremiumBilling": false }""")])
            .UseFeatureFlags(flags => [flags.Register<BillingFlags>()]));

        Assert.Throws<InvalidOperationException>(() => mgr.GetFeatureFlagsForTenant<BillingFlags>("ghost"));
    }
}
