using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers; // FromStaticJson / FromStatic
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// (P5) DI exclusion: a type whose every rule is <c>.TenantScoped()</c> has no global value and must NOT be
/// registered for injection (injecting it would freeze one tenant into a long-lived consumer — the captive
/// dependency bug ADR-005 §5 avoids). A type that also has a global base rule stays injectable.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantDiExclusionTests
{
    public sealed record GlobalBase { public string Region { get; init; } = ""; }
    public sealed record TenantOnly { public string V { get; init; } = ""; }

    [Fact]
    public void TenantOnlyType_IsExcludedFromGlobalDiPlan_GlobalBaseTypeStaysInjectable()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => new ConfigRule[]
        {
            // Has a global base AND a tenant overlay -> the global base value is injectable.
            rules.For<GlobalBase>().FromStaticJson("""{ "Region": "base" }"""),
            rules.For<GlobalBase>().FromStatic(a => new GlobalBase { Region = a.Tenant! }).TenantScoped(),

            // ONLY tenant-scoped -> no global value -> excluded from the DI plan.
            rules.For<TenantOnly>().FromStatic(a => new TenantOnly { V = a.Tenant! }).TenantScoped(),
        }));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        // The global-base type resolves to its global value (tenant overlay skipped in the global pipeline).
        var globalBase = sp.GetService<GlobalBase>();
        Assert.NotNull(globalBase);
        Assert.Equal("base", globalBase!.Region);

        // The purely tenant-scoped type is not injectable — obtain it via GetConfigForTenant<T>(id) instead.
        Assert.Null(sp.GetService<TenantOnly>());
    }
}
