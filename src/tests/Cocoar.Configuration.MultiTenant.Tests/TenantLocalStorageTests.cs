using System.Collections.Concurrent;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.LocalStorage;
using Cocoar.Configuration.Providers; // FromObservable / FromLocalStorage / IStorageBackend / GetLocalStorageForTenant

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>In-memory backend so each tenant gets an isolated, file-free overlay store.</summary>
internal sealed class InMemoryBackend : IStorageBackend
{
    private byte[]? _data;

    public Task<byte[]?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(_data);

    public Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        _data = data;
        return Task.CompletedTask;
    }
}

/// <summary>
/// (P4) per-tenant LocalStorage through the real API: each tenant's overlay uses its own backend (the factory
/// overload keys the store by accessor.Tenant), so a write to one tenant's overlay leaves the others untouched,
/// and provenance is computed per tenant. Ports the POC's TenantLocalStoragePocTests.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantLocalStorageTests
{
    public sealed record Smtp
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
    }

    [Fact]
    public async Task LocalStorageOverlay_IsPerTenant_WithDistinctBackends()
    {
        var backends = new ConcurrentDictionary<string, InMemoryBackend>();
        IStorageBackend BackendFor(string? tenant) => backends.GetOrAdd(tenant ?? "", _ => new InMemoryBackend());

        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Smtp>().FromObservable("""{ "Host": "smtp.default.com", "Port": 25 }"""),
                rules.For<Smtp>().FromLocalStorage((a, _) => BackendFor(a.Tenant)).TenantScoped(),
            ])
            .UseDebounce(25));

        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("A");
        await tenants.InitializeTenantAsync("B");

        // Tenant A writes Port=587 into ITS OWN overlay store.
        var storageA = mgr.GetLocalStorageForTenant<Smtp>("A");
        await storageA.SetAsync(x => x.Port, 587);
        await TenantWait.UntilAsync(() => mgr.GetConfigForTenant<Smtp>("A")?.Port == 587, "tenant A override applied");

        // (1) A's effective value reflects the write; Host is inherited from the base (sparse overlay).
        var configA = mgr.GetConfigForTenant<Smtp>("A")!;
        Assert.Equal(587, configA.Port);
        Assert.Equal("smtp.default.com", configA.Host);

        // (2) Tenant B is UNAFFECTED — its store is a distinct backend instance.
        Assert.Equal(25, mgr.GetConfigForTenant<Smtp>("B")!.Port);
        Assert.Null(await mgr.GetLocalStorageForTenant<Smtp>("B").ReadAsync());

        // (3) Base-vs-effective provenance for A, computed over the tenant pipeline.
        var entries = await storageA.DescribeAsync();
        var port = Assert.Single(entries, e => e.KeyPath == "Port");
        Assert.True(port.IsOverridden);
        Assert.Equal(25, port.BaseValue!.Value.GetInt32());
        Assert.Equal(587, port.EffectiveValue!.Value.GetInt32());
    }
}
