using System.Text;
using global::Marten;

namespace Cocoar.Configuration.Marten.Tests;

/// <summary>
/// Integration tests for <see cref="MartenStoreBackend"/> against a real PostgreSQL instance. They self-skip
/// when Docker is unavailable (see <see cref="PostgresFixture"/>), so the default suite stays green everywhere.
/// </summary>
[Trait("Type", "Integration")]
public sealed class MartenStoreBackendTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [SkippableFact]
    public async Task Write_then_read_round_trips_the_overlay_bytes()
    {
        Skip.IfNot(pg.Available, pg.SkipReason ?? "Docker not available");
        await using var store = SingleTenantStore(pg.DefaultConnectionString);
        var backend = new MartenStoreBackend(store);

        const string key = "MyApp.Settings.SmtpSettings";
        var data = """{"Smtp":{"Port":587}}"""u8.ToArray();

        await backend.WriteAsync(key, data);
        var read = await backend.ReadAsync(key);

        Assert.NotNull(read);
        Assert.Equal(Encoding.UTF8.GetString(data), Encoding.UTF8.GetString(read));
    }

    [SkippableFact]
    public async Task Read_of_a_missing_key_returns_null()
    {
        Skip.IfNot(pg.Available, pg.SkipReason ?? "Docker not available");
        await using var store = SingleTenantStore(pg.DefaultConnectionString);
        var backend = new MartenStoreBackend(store);

        var read = await backend.ReadAsync("Never.Written.Key");

        Assert.Null(read);
    }

    [SkippableFact]
    public async Task Writes_are_isolated_per_tenant_database()
    {
        Skip.IfNot(pg.Available, pg.SkipReason ?? "Docker not available");
        await using var store = MultiTenantStore();
        var backendA = new MartenStoreBackend(store, PostgresFixture.TenantA);
        var backendB = new MartenStoreBackend(store, PostgresFixture.TenantB);

        const string key = "MyApp.Settings.TenantSettings";
        var dataA = """{"Theme":"dark"}"""u8.ToArray();

        await backendA.WriteAsync(key, dataA);

        // Same key, different tenant database -> invisible to tenant B (database-per-tenant isolation).
        Assert.Null(await backendB.ReadAsync(key));

        // Tenant A reads back its own write.
        var readA = await backendA.ReadAsync(key);
        Assert.NotNull(readA);
        Assert.Equal(Encoding.UTF8.GetString(dataA), Encoding.UTF8.GetString(readA));
    }

    private static IDocumentStore SingleTenantStore(string connectionString)
        => DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.RegisterDocumentType<CocoarConfigDocument>();
        });

    private IDocumentStore MultiTenantStore()
        => DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabases(x =>
            {
                x.AddSingleTenantDatabase(pg.ConnectionStringForTenantA, PostgresFixture.TenantA);
                x.AddSingleTenantDatabase(pg.ConnectionStringForTenantB, PostgresFixture.TenantB);
            });
            opts.RegisterDocumentType<CocoarConfigDocument>();
        });
}
