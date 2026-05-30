using Cocoar.Configuration.Reactive;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Tenant-scoped configuration lifecycle and access (ADR-005). Implemented by <c>ConfigManager</c> alongside
/// <see cref="IConfigurationAccessor"/>; the existing global surface stays byte-identical.
/// <para>
/// The active-tenant list is owned by the host (e.g. db-per-tenant). This surface materializes a single
/// tenant's configuration <b>on demand</b> and never enumerates or syncs a tenant registry. Each initialized
/// tenant is a pipeline bundle layered on the shared global base: the effective value for a tenant is
/// <c>[global rules] ++ [tenant-scoped rules]</c>, tenant winning per key and inheriting the rest.
/// </para>
/// <para>
/// Async is confined to the explicit init moment (<see cref="InitializeTenantAsync"/>); afterwards reads
/// (<see cref="GetConfigForTenant{T}"/>) are synchronous, exactly like the global config.
/// </para>
/// </summary>
public interface ITenantConfigurationAccessor
{
    /// <summary>
    /// Builds and initializes the tenant's pipeline (async). Idempotent: a tenant that is already initialized
    /// (or whose initialization is in flight) is returned without rebuilding. Call at tenant creation.
    /// </summary>
    Task InitializeTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent warmup — ensures the tenant is initialized, building it if needed. Identical semantics to
    /// <see cref="InitializeTenantAsync"/>; use at request start (middleware) where intent is "make sure it's ready".
    /// </summary>
    Task EnsureTenantInitializedAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> once the tenant's pipeline has been built and its initial snapshot published.</summary>
    bool IsTenantInitialized(string tenantId);

    /// <summary>
    /// Gets the tenant's current configuration value for <typeparamref name="T"/> (synchronous).
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenant has not been initialized.</exception>
    T? GetConfigForTenant<T>(string tenantId) where T : class;

    /// <summary>
    /// Gets a reactive wrapper over the tenant's configuration value for <typeparamref name="T"/>. Emits the
    /// current value on subscribe and on every subsequent change to THIS tenant's effective value (replay-1).
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenant has not been initialized.</exception>
    IReactiveConfig<T> GetReactiveConfigForTenant<T>(string tenantId);

    /// <summary>
    /// Disposes the tenant's pipeline bundle and forgets the tenant. Drains any in-flight recompute. Call at
    /// tenant removal. A no-op for an unknown/already-removed tenant.
    /// </summary>
    Task RemoveTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}
