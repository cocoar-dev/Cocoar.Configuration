using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// Tuples whose element types have different *rule scopes* are fully supported — "scope" is a property of a
/// rule (<c>.TenantScoped()</c>), not of a type. Each element is read from the relevant pipeline's atomic
/// snapshot: the global accessor skips tenant overlays; the per-tenant accessor gives effective values. The
/// only error case is a GLOBAL tuple containing a type whose EVERY rule is tenant-scoped (no global value).
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public sealed class MixedScopeTupleTests
{
    public sealed class Gl { public string V { get; set; } = ""; }          // only a global rule
    public sealed class Mix { public string V { get; set; } = ""; }         // global base + tenant overlay
    public sealed class TenantOnly { public string V { get; set; } = ""; }  // ONLY a .TenantScoped() rule

    private static ConfigManager Build() => ConfigManager.Create(c => c
        .UseConfiguration(rules =>
        [
            rules.For<Gl>().FromStaticJson("""{ "V": "global" }"""),
            rules.For<Mix>().FromStaticJson("""{ "V": "mix-base" }"""),
            rules.For<Mix>().FromStatic(x => new Mix { V = $"mix-{x.Tenant}" }).TenantScoped(),
            rules.For<TenantOnly>().FromStatic(x => new TenantOnly { V = $"to-{x.Tenant}" }).TenantScoped(),
        ])
        .UseDebounce(25));

    [Fact]
    public async Task PerTenant_MixedScope_ReadsGlobalAndTenantEffective()
    {
        using var mgr = Build();
        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");

        var (g, m) = tenants.GetReactiveConfigForTenant<(Gl, Mix)>("acme").CurrentValue;
        Assert.Equal("global", g.V);     // global-only type → global value
        Assert.Equal("mix-acme", m.V);   // mixed type → tenant-effective value
    }

    [Fact]
    public void Global_MixedScope_SkipsTenantOverlay()
    {
        using var mgr = Build();

        var (g, m) = mgr.GetReactiveConfig<(Gl, Mix)>().CurrentValue;
        Assert.Equal("global", g.V);     // global value
        Assert.Equal("mix-base", m.V);   // base value, tenant overlay skipped
    }

    [Fact]
    public void Global_WithTenantOnlyType_ThrowsTargetedError()
    {
        using var mgr = Build();

        // A type with ONLY .TenantScoped() rules has no value in the global pipeline → targeted error.
        var ex = Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(Gl, TenantOnly)>());
        Assert.Contains("only .TenantScoped() rules", ex.Message);
        Assert.Contains("GetReactiveConfigForTenant", ex.Message);
    }
}
