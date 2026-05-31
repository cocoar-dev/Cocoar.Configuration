using Cocoar.Configuration.Core;
using Cocoar.Configuration.WritableStore;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Per-tenant WritableStore write facade (ADR-005 §7). The global write facade is <c>IWritableStore&lt;T&gt;</c>
/// resolved from DI; the per-tenant facade is obtained explicitly with the tenant id, exactly like the rest of
/// the tenant surface (<c>GetConfigForTenant</c>, <c>GetFeatureFlagsForTenant</c>, …).
/// </summary>
public static class TenantWritableStoreExtensions
{
    /// <summary>
    /// Returns the WritableStore write facade for a tenant's overlay of <typeparamref name="T"/>. Writes target
    /// the tenant pipeline's own store and trigger only that tenant's recompute. For per-tenant isolation the
    /// rule must use a per-tenant backend, e.g.
    /// <c>rules.For&lt;T&gt;().FromStore(a =&gt; BackendFor(a.Tenant)).TenantScoped()</c> — the factory overload
    /// keys its store by <c>accessor.Tenant</c>, so each tenant gets its own store/backend.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant is not initialized, or it has no WritableStore rule for <typeparamref name="T"/>.
    /// </exception>
    public static IWritableStore<T> GetWritableStoreForTenant<T>(this ConfigManager manager, string tenantId)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var pipeline = manager.GetInitializedTenantPipeline(tenantId);

        // The tenant pipeline's store for T (last WritableStore rule wins — highest-precedence overlay).
        WritableStoreState? store = null;
        foreach (var ruleManager in pipeline.RuleManagers)
        {
            if (ruleManager.CurrentProvider is WritableStoreProvider provider
                && provider.Store.ConfigurationType == typeof(T))
            {
                store = provider.Store;
            }
        }

        if (store is null)
        {
            throw new InvalidOperationException(
                $"No WritableStore rule is registered for '{typeof(T).Name}' in tenant '{tenantId}'. " +
                $"Add rules.For<{typeof(T).Name}>().FromStore(...).TenantScoped().");
        }

        // The TenantPipeline is the IWritableStoreHost: base/effective JSON is computed over the tenant's own
        // rule managers and snapshot, so provenance and sparse-write key alignment are per tenant.
        return new WritableStoreAdapter<T>(pipeline, store);
    }
}
