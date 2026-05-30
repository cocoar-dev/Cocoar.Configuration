using Cocoar.Configuration.Core;
using Cocoar.Configuration.LocalStorage;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Per-tenant LocalStorage write facade (ADR-005 §7). The global write facade is <c>ILocalStorage&lt;T&gt;</c>
/// resolved from DI; the per-tenant facade is obtained explicitly with the tenant id, exactly like the rest of
/// the tenant surface (<c>GetConfigForTenant</c>, <c>GetFeatureFlagsForTenant</c>, …).
/// </summary>
public static class TenantLocalStorageExtensions
{
    /// <summary>
    /// Returns the LocalStorage write facade for a tenant's overlay of <typeparamref name="T"/>. Writes target
    /// the tenant pipeline's own store and trigger only that tenant's recompute. For per-tenant isolation the
    /// rule must use a per-tenant backend, e.g.
    /// <c>rules.For&lt;T&gt;().FromLocalStorage(a =&gt; BackendFor(a.Tenant)).TenantScoped()</c> — the factory overload
    /// keys its store by <c>accessor.Tenant</c>, so each tenant gets its own store/backend.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant is not initialized, or it has no LocalStorage rule for <typeparamref name="T"/>.
    /// </exception>
    public static ILocalStorage<T> GetLocalStorageForTenant<T>(this ConfigManager manager, string tenantId)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var pipeline = manager.GetInitializedTenantPipeline(tenantId);

        // The tenant pipeline's store for T (last LocalStorage rule wins — highest-precedence overlay).
        LocalStorageStore? store = null;
        foreach (var ruleManager in pipeline.RuleManagers)
        {
            if (ruleManager.CurrentProvider is LocalStorageProvider provider
                && provider.Store.ConfigurationType == typeof(T))
            {
                store = provider.Store;
            }
        }

        if (store is null)
        {
            throw new InvalidOperationException(
                $"No LocalStorage rule is registered for '{typeof(T).Name}' in tenant '{tenantId}'. " +
                $"Add rules.For<{typeof(T).Name}>().FromLocalStorage(...).TenantScoped().");
        }

        // The TenantPipeline is the ILocalStorageHost: base/effective JSON is computed over the tenant's own
        // rule managers and snapshot, so provenance and sparse-write key alignment are per tenant.
        return new LocalStorageAdapter<T>(pipeline, store);
    }
}
