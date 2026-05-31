using System.Reactive.Subjects;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers; // FromObservable / FromStatic / FromStaticJson

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// (e) tenant removal + dispose ordering: a removed tenant is forgotten (reads throw), can be rebuilt, and a
/// removed/idle tenant stops tracking the global base (its pipeline — engine, subscriptions, backplane — is
/// disposed). Disposing the manager tears down all tenants. Concurrent init/remove never deadlocks or corrupts.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantLifecycleTests
{
    public sealed record Geo { public string Region { get; init; } = ""; }

    public sealed record Smtp
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
    }

    [Fact]
    public async Task RemoveTenant_ForgetsTenant_ThenReInitRebuilds()
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
        Assert.Equal("acme", mgr.GetConfigForTenant<Geo>("acme")!.Region);

        await tenants.RemoveTenantAsync("acme");
        Assert.False(tenants.IsTenantInitialized("acme"));
        Assert.Throws<InvalidOperationException>(() => mgr.GetConfigForTenant<Geo>("acme"));

        // Re-init builds a fresh pipeline.
        await tenants.EnsureTenantInitializedAsync("acme");
        Assert.True(tenants.IsTenantInitialized("acme"));
        Assert.Equal("acme", mgr.GetConfigForTenant<Geo>("acme")!.Region);
    }

    [Fact]
    public async Task RemoveUnknownTenant_IsNoOp()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<Geo>().FromStaticJson("""{ "Region": "base" }""")]));

        await ((ITenantConfigurationAccessor)mgr).RemoveTenantAsync("ghost"); // must not throw
    }

    [Fact]
    public async Task RemovedTenant_PipelineIsDisposed()
    {
        using var sharedBase = new BehaviorSubject<string>("""{ "Host": "smtp.global", "Port": 25 }""");

        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<Smtp>().FromObservable(sharedBase)])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");

        var reactive = tenants.GetReactiveConfigForTenant<Smtp>("acme");
        Assert.Equal(25, reactive.CurrentValue.Port);

        await tenants.RemoveTenantAsync("acme");

        // RemoveTenantAsync drains the in-flight recompute and disposes the pipeline (engine + subscriptions +
        // backplane). A stale reactive handle held across removal now observes a disposed backplane — proving the
        // teardown actually happened (vs. a leaked, still-live pipeline).
        Assert.False(tenants.IsTenantInitialized("acme"));
        Assert.Throws<ObjectDisposedException>(() => _ = reactive.CurrentValue);
    }

    [Fact]
    public async Task DisposeManager_TearsDownTenants()
    {
        var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<Geo>().FromStaticJson("""{ "Region": "base" }""")])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");
        await tenants.InitializeTenantAsync("globex");

        await mgr.DisposeAsync();

        Assert.False(tenants.IsTenantInitialized("acme"));
        Assert.False(tenants.IsTenantInitialized("globex"));
    }

    [Fact]
    public async Task ConcurrentInitAndRemove_NoDeadlockOrCorruption()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Geo>().FromStaticJson("""{ "Region": "base" }"""),
                rules.For<Geo>().FromStatic(a => new Geo { Region = a.Tenant! }).TenantScoped(),
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;

        // Hammer init + remove across a handful of tenants concurrently; must not throw, deadlock, or leak state.
        var work = new List<Task>();
        for (var i = 0; i < 8; i++)
        {
            var id = $"t{i % 3}";
            work.Add(Task.Run(() => tenants.EnsureTenantInitializedAsync(id)));
            work.Add(Task.Run(() => tenants.RemoveTenantAsync(id)));
        }
        await Task.WhenAll(work);

        // The manager is still usable afterwards: a fresh init resolves correctly.
        await tenants.EnsureTenantInitializedAsync("final");
        Assert.Equal("final", mgr.GetConfigForTenant<Geo>("final")!.Region);
    }
}
