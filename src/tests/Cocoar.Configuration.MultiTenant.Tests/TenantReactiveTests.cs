using System.Reactive.Subjects;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers; // FromObservable / FromStaticJson / FromStatic

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// (d) per-tenant reactive: <c>GetReactiveConfigForTenant&lt;T&gt;</c> tracks the tenant's effective value
/// (single type and same-scope tuple), and the global reactive path is unchanged.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantReactiveTests
{
    public sealed record Smtp
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
    }

    public sealed record A { public string V { get; init; } = ""; }
    public sealed record B { public int N { get; init; } }

    [Fact]
    public async Task GetReactiveConfigForTenant_EmitsTenantValue_AndUpdatesOnBaseChange()
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

        var reactive = tenants.GetReactiveConfigForTenant<Smtp>("acme");
        Assert.Equal("smtp.tenant", reactive.CurrentValue.Host); // overlay wins
        Assert.Equal(25, reactive.CurrentValue.Port);            // inherited from base

        var received = new List<Smtp>();
        using var sub = reactive.Subscribe(received.Add);

        sharedBase.OnNext("""{ "Host": "smtp.global", "Port": 2525 }""");
        await TenantWait.UntilAsync(() => reactive.CurrentValue.Port == 2525, "tenant reactive sees base change");

        Assert.Equal("smtp.tenant", reactive.CurrentValue.Host);   // override survived the base change
        Assert.Contains(received, v => v.Port == 2525);            // subscriber was notified
    }

    [Fact]
    public async Task GetReactiveConfigForTenant_Tuple_ReadsTenantValues_GlobalReadsBase()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<A>().FromStaticJson("""{ "V": "base" }"""),
                rules.For<A>().FromStatic(x => new A { V = x.Tenant! }).TenantScoped(),
                rules.For<B>().FromStaticJson("""{ "N": 1 }"""),
                rules.For<B>().FromStatic(_ => new B { N = 9 }).TenantScoped(),
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");

        // Tenant tuple reactive reads the tenant's own accessor + backplane (loosened ReactiveTupleConfig).
        var tenantReactive = tenants.GetReactiveConfigForTenant<(A, B)>("acme");
        var (a, b) = tenantReactive.CurrentValue;
        Assert.Equal("acme", a.V);
        Assert.Equal(9, b.N);

        // Global tuple reactive is unchanged: overlays skipped -> base values.
        var globalReactive = mgr.GetReactiveConfig<(A, B)>();
        Assert.Equal("base", globalReactive.CurrentValue.Item1.V);
        Assert.Equal(1, globalReactive.CurrentValue.Item2.N);
    }
}
