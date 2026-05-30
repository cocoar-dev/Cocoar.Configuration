using System.Reactive.Subjects;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers; // FromObservable

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// Fan-out: a change to the shared global base must reach already-initialized tenants. In the v1 model each
/// tenant pipeline runs the full flat rule list with its OWN provider instances and OWN change subscriptions,
/// so a live base source (file/observable/http) propagates to every initialized tenant automatically — no
/// cross-pipeline coordinator required. This test pins that property (see ADR-005 §6 and the (c) note).
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantFanOutTests
{
    public sealed record Smtp
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
    }

    [Fact]
    public async Task GlobalBaseChange_FansOutToInitializedTenants_TenantOverrideSurvives()
    {
        using var sharedBase = new BehaviorSubject<string>("""{ "Host": "smtp.global", "Port": 25 }""");

        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Smtp>().FromObservable(sharedBase),
                rules.For<Smtp>().FromObservable("""{ "Host": "smtp.tenant" }""").TenantScoped(),
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");
        await tenants.InitializeTenantAsync("globex");

        Assert.Equal(25, mgr.GetConfigForTenant<Smtp>("acme")!.Port);
        Assert.Equal(25, mgr.GetConfigForTenant<Smtp>("globex")!.Port);

        // Global base change Port 25 -> 2525 fans out to BOTH initialized tenants.
        sharedBase.OnNext("""{ "Host": "smtp.global", "Port": 2525 }""");

        await TenantWait.UntilAsync(() => mgr.GetConfigForTenant<Smtp>("acme")?.Port == 2525, "acme sees base change");
        await TenantWait.UntilAsync(() => mgr.GetConfigForTenant<Smtp>("globex")?.Port == 2525, "globex sees base change");

        // Tenant override survives the base change; the global pipeline also tracks it.
        Assert.Equal("smtp.tenant", mgr.GetConfigForTenant<Smtp>("acme")!.Host);
        Assert.Equal("smtp.global", mgr.GetConfig<Smtp>()!.Host);
        Assert.Equal(2525, mgr.GetConfig<Smtp>()!.Port);
    }
}
