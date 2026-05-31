using System.Collections.Concurrent;
using System.Linq;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.WritableStore;
using Cocoar.Configuration.Providers; // FromObservable / FromStore / GetWritableStoreForTenant

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// Concurrency proof for the per-tenant WritableStore: writes on one tenant never bleed into another, and a
/// read of any tenant — even while other tenants are being hammered with writes — is always a whole, consistent
/// snapshot (the sparse overlay merges atomically; Host always inherits the base, Port is only ever this
/// tenant's own value or the base). Closes the isolation/atomicity-under-concurrency gap noted in the review.
/// Reuses <c>InMemoryBackend</c> (defined in TenantWritableStoreTests) so each tenant gets its own file-free store.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Concurrency")]
public class TenantWritableStoreConcurrencyTests
{
    public sealed record Smtp
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
    }

    private static ConfigManager Build()
    {
        var backends = new ConcurrentDictionary<string, InMemoryBackend>();
        return ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Smtp>().FromObservable("""{ "Host": "smtp.default.com", "Port": 25 }"""),
                rules.For<Smtp>().FromStore((a, _) => backends.GetOrAdd(a.Tenant ?? "", _ => new InMemoryBackend()))
                                 .TenantScoped(),
            ])
            .UseDebounce(25));
    }

    [Fact]
    public async Task ConcurrentWritesAcrossTenants_StayIsolated_AndReadsAreNeverTorn()
    {
        const int tenantCount = 8;
        const int writesPerTenant = 40;

        using var mgr = Build();
        var tenants = (ITenantConfigurationAccessor)mgr;

        // Tenant ti always writes Port = i (a fixed per-tenant value), so ordering is irrelevant and the only
        // legal values a reader of ti may ever observe are 25 (base) or i (ti's own overlay).
        var ids = Enumerable.Range(0, tenantCount).Select(i => $"t{i}").ToArray();
        foreach (var id in ids)
        {
            await tenants.InitializeTenantAsync(id);
        }

        using var cts = new CancellationTokenSource();
        var readerErrors = new ConcurrentQueue<string>();

        var readers = ids.Select(id =>
        {
            var own = int.Parse(id[1..]);
            return Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var cfg = mgr.GetConfigForTenant<Smtp>(id);
                    if (cfg is not null)
                    {
                        // Host is never overridden → must always be the inherited base (no torn snapshot).
                        if (cfg.Host != "smtp.default.com")
                        {
                            readerErrors.Enqueue($"{id}: torn Host '{cfg.Host}'");
                        }
                        // Port is either the base or THIS tenant's own value — never another tenant's.
                        if (cfg.Port != 25 && cfg.Port != own)
                        {
                            readerErrors.Enqueue($"{id}: cross-tenant Port {cfg.Port} (legal: 25 or {own})");
                        }
                    }

                    await Task.Yield();
                }
            });
        }).ToArray();

        var writers = ids.Select(id =>
        {
            var value = int.Parse(id[1..]);
            var store = mgr.GetWritableStoreForTenant<Smtp>(id);
            return Task.Run(async () =>
            {
                for (var w = 0; w < writesPerTenant; w++)
                {
                    await store.SetAsync(x => x.Port, value);
                }
            });
        }).ToArray();

        await Task.WhenAll(writers);

        foreach (var id in ids)
        {
            var own = int.Parse(id[1..]);
            await TenantWait.UntilAsync(() => mgr.GetConfigForTenant<Smtp>(id)?.Port == own, $"{id} converged to {own}");
        }

        cts.Cancel();
        await Task.WhenAll(readers);

        Assert.True(readerErrors.IsEmpty, string.Join(" | ", readerErrors));

        // Final state: each tenant holds its own value, Host inherited, nothing corrupted.
        foreach (var id in ids)
        {
            var cfg = mgr.GetConfigForTenant<Smtp>(id)!;
            Assert.Equal(int.Parse(id[1..]), cfg.Port);
            Assert.Equal("smtp.default.com", cfg.Host);
        }
    }

    [Fact]
    public async Task ConcurrentWritesToSameTenant_SerializeWithoutCorruptionOrDeadlock()
    {
        const int writes = 60;

        using var mgr = Build();
        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("solo");

        var store = mgr.GetWritableStoreForTenant<Smtp>("solo");

        // Hammer one tenant's overlay with concurrent writes of distinct values — must serialize through the
        // store's write lock without throwing, deadlocking, or corrupting the persisted JSON.
        var tasks = Enumerable.Range(1, writes).Select(v => store.SetAsync(x => x.Port, v));
        await Task.WhenAll(tasks);

        // Converges to SOME written value; Host stays inherited; the overlay remains a valid sparse override.
        await TenantWait.UntilAsync(
            () => mgr.GetConfigForTenant<Smtp>("solo")?.Port is >= 1 and <= writes,
            "solo converged to a written value");

        var cfg = mgr.GetConfigForTenant<Smtp>("solo")!;
        Assert.InRange(cfg.Port, 1, writes);
        Assert.Equal("smtp.default.com", cfg.Host);
    }
}
