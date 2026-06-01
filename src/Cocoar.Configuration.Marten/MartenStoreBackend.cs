using System.Text;
using Cocoar.Configuration.Providers;
using global::Marten;

namespace Cocoar.Configuration.Marten;

/// <summary>
/// An <see cref="IStoreBackend"/> that persists WritableStore overlays as <see cref="CocoarConfigDocument"/>
/// documents in a Marten (PostgreSQL) store. One document per configuration type, keyed by the type's full name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tenant routing.</b> When constructed with a non-empty <c>tenantId</c>, every read and write opens its session
/// for that tenant. With Marten database-per-tenant (a multi-tenanted <see cref="IDocumentStore"/>) this routes the
/// document into the tenant's own database — so each tenant's configuration lives in its own DB. A <c>null</c>/blank
/// tenant uses Marten's default tenant (the single-database case).
/// </para>
/// <para>
/// The backend is intentionally stateless and cheap to construct: it holds the (DI-singleton) document store and a
/// tenant id, and opens a short-lived session per operation. That is exactly what the service-backed
/// <c>FromStore</c> rule expects, so it can re-create the backend each recompute without connection-pool churn
/// (Marten/Npgsql owns the pool).
/// </para>
/// </remarks>
public sealed class MartenStoreBackend : IStoreBackend
{
    private readonly IDocumentStore _store;
    private readonly string? _tenantId;

    /// <summary>
    /// Creates a backend over the given Marten <paramref name="store"/>, optionally bound to a tenant.
    /// </summary>
    /// <param name="store">The Marten document store, typically resolved from DI as a singleton.</param>
    /// <param name="tenantId">The tenant whose database to read from / write to. <c>null</c> or blank uses
    /// Marten's default tenant. Pass <c>accessor.Tenant</c> from a tenant-scoped rule.</param>
    public MartenStoreBackend(IDocumentStore store, string? tenantId = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var session = OpenQuerySession();
        var document = await session.LoadAsync<CocoarConfigDocument>(key, ct).ConfigureAwait(false);
        return document?.Json is { } json ? Encoding.UTF8.GetBytes(json) : null;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(data);

        await using var session = OpenSession();
        // Store is an upsert keyed by Id; SaveChangesAsync commits the single document in one transaction.
        session.Store(new CocoarConfigDocument { Id = key, Json = Encoding.UTF8.GetString(data) });
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private IQuerySession OpenQuerySession()
        => _tenantId is null ? _store.QuerySession() : _store.QuerySession(_tenantId);

    private IDocumentSession OpenSession()
        => _tenantId is null ? _store.LightweightSession() : _store.LightweightSession(_tenantId);
}
