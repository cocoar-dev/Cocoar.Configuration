using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers; // FromObservable / FromStaticJson / FromStatic

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// (b2) acceptance — the real engine (per-tenant <c>TenantPipeline</c> on the shared global base), exercised
/// through the production <see cref="ITenantConfigurationAccessor"/> API. Replaces the POC's standalone-manager fake.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantConfigTests
{
    public sealed record Smtp
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
    }

    public sealed record Geo
    {
        public string Region { get; init; } = "";
    }

    /// <summary>
    /// Core per-tenant mechanism on the real engine: a shared global base, a <c>.TenantScoped()</c> sparse overlay
    /// that wins per key while inheriting the rest in the tenant pipeline, and is skipped in the global pipeline.
    /// </summary>
    [Fact]
    public async Task GetConfigForTenant_OverlayWinsPerKey_GlobalReadsBase()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Smtp>().FromObservable("""{ "Host": "smtp.global", "Port": 25 }"""),
                rules.For<Smtp>().FromObservable("""{ "Host": "smtp.tenant" }""").TenantScoped(),
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");

        // Global pipeline: tenant overlay is skipped (no tenant) -> base value wins.
        Assert.Equal("smtp.global", mgr.GetConfig<Smtp>()!.Host);
        Assert.Equal(25, mgr.GetConfig<Smtp>()!.Port);

        // Tenant pipeline: overlay wins on Host, Port INHERITED from base (sparse deep-merge).
        var acme = mgr.GetConfigForTenant<Smtp>("acme")!;
        Assert.Equal("smtp.tenant", acme.Host);
        Assert.Equal(25, acme.Port);

        Assert.True(tenants.IsTenantInitialized("acme"));
        Assert.False(tenants.IsTenantInitialized("nope"));
    }

    /// <summary>
    /// The tenant id flows into a tenant-varying rule factory (<c>FromStatic(a =&gt; ... a.Tenant ...)</c>), so two
    /// tenants resolve different values from the SAME rule list. The global pipeline keeps the base (overlay skipped).
    /// </summary>
    [Fact]
    public async Task TenantId_FlowsIntoRuleFactory_PerTenantValues()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Geo>().FromStaticJson("""{ "Region": "base" }"""),
                rules.For<Geo>().FromStatic(a => new Geo { Region = a.Tenant! }).TenantScoped(),
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");
        await tenants.InitializeTenantAsync("globex");

        Assert.Equal("base", mgr.GetConfig<Geo>()!.Region);            // global: overlay skipped
        Assert.Equal("acme", mgr.GetConfigForTenant<Geo>("acme")!.Region);
        Assert.Equal("globex", mgr.GetConfigForTenant<Geo>("globex")!.Region);
    }

    /// <summary>Init is idempotent and safe under concurrent callers — a tenant is built exactly once.</summary>
    [Fact]
    public async Task InitializeTenant_IsIdempotent_AndConcurrencySafe()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<Geo>().FromStaticJson("""{ "Region": "base" }""")])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;

        // 32 concurrent init calls for the same tenant must all succeed and observe the same pipeline.
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => tenants.EnsureTenantInitializedAsync("acme")));

        Assert.True(tenants.IsTenantInitialized("acme"));
        Assert.Equal("base", mgr.GetConfigForTenant<Geo>("acme")!.Region);
    }

    public sealed record Security { public bool MfaRequired { get; init; } }

    /// <summary>
    /// "Non-negotiable" platform ceiling: a global rule placed AFTER the tenant overlay wins over the tenant — no
    /// special tier, just list position (ADR-005 §3). The classic case: a tenant tries to disable MFA, the
    /// platform forces it back on. In the global pipeline the tenant overlay is skipped, so the ceiling holds too.
    /// </summary>
    [Fact]
    public async Task NonNegotiableGlobalRuleAfterTenantOverlay_WinsOverTenant()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Security>().FromStaticJson("""{ "MfaRequired": false }"""),                 // base
                rules.For<Security>().FromStatic(_ => new Security { MfaRequired = false }).TenantScoped(), // tenant tries to keep MFA off
                rules.For<Security>().FromStaticJson("""{ "MfaRequired": true }"""),                  // platform ceiling (last → wins)
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");

        Assert.True(mgr.GetConfigForTenant<Security>("acme")!.MfaRequired); // ceiling beats the tenant
        Assert.True(mgr.GetConfig<Security>()!.MfaRequired);                // and holds globally
    }

    /// <summary>
    /// The tenant gate is robust to fluent ordering: <c>.TenantScoped().When(p)</c> (where <c>.When</c> overwrites
    /// the composed predicate) still skips the rule in the global pipeline — the static TenantScoped marker, not
    /// the predicate, enforces "no tenant ⇒ skip". The custom predicate still applies per tenant.
    /// </summary>
    [Fact]
    public async Task TenantScoped_ThenWhen_StillSkipsGlobally_AndPredicateAppliesPerTenant()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Geo>().FromStaticJson("""{ "Region": "base" }"""),
                rules.For<Geo>().FromStatic(a => new Geo { Region = a.Tenant! })
                     .TenantScoped()
                     .When(a => a.Tenant == "acme"),   // wrong order on purpose; predicate also gates to "acme"
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");
        await tenants.InitializeTenantAsync("other");

        Assert.Equal("base", mgr.GetConfig<Geo>()!.Region);                 // global: skipped via static gate
        Assert.Equal("acme", mgr.GetConfigForTenant<Geo>("acme")!.Region);  // acme: predicate true → overlay
        Assert.Equal("base", mgr.GetConfigForTenant<Geo>("other")!.Region); // other: predicate false → base
    }

    /// <summary>Reading a tenant that was never initialized is a clear error, not a silent null.</summary>
    [Fact]
    public void GetConfigForTenant_BeforeInit_Throws()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<Geo>().FromStaticJson("""{ "Region": "base" }""")]));

        Assert.Throws<InvalidOperationException>(() => mgr.GetConfigForTenant<Geo>("ghost"));
    }
}
