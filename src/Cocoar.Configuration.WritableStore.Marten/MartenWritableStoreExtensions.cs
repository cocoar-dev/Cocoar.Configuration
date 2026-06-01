using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using global::Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.WritableStore.Marten;

/// <summary>
/// Service-backed (Layer-2, ADR-006) rule factory that backs a WritableStore with Marten. Valid only inside
/// <c>UseServiceBackedConfiguration(...)</c> — the rule stays dormant until the host starts and resolves the Marten
/// <see cref="IDocumentStore"/> from the application container at recompute time.
/// </summary>
public static class MartenWritableStoreExtensions
{
    /// <summary>
    /// Backs the configuration type with a Marten (<see cref="MartenStoreBackend"/>) WritableStore. The Marten
    /// <see cref="IDocumentStore"/> is resolved from DI and the current <c>accessor.Tenant</c> selects the tenant
    /// database — combine with <c>.TenantScoped()</c> for per-tenant, database-per-tenant configuration:
    /// <code>
    /// builder.UseServiceBackedConfiguration(rules =>
    /// [
    ///     rules.For&lt;TenantSettings&gt;().FromMartenStore().TenantScoped().Build(),
    /// ]);
    /// </code>
    /// This also exposes the <c>IWritableStore&lt;TenantSettings&gt;</c> write facade (per tenant), since it reuses
    /// the tenant-keyed WritableStore backend pipeline.
    /// </summary>
    /// <typeparam name="T">The configuration type to populate from Marten.</typeparam>
    /// <param name="builder">The service-backed provider builder (from <c>UseServiceBackedConfiguration</c>).</param>
    public static ProviderRuleBuilder<WritableStoreProvider, WritableStoreProviderOptions, WritableStoreProviderQueryOptions>
        FromMartenStore<T>(this ServiceBackedProviderBuilder<T> builder)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Reuse the DI package's tenant-keyed, sp-gated FromStore seam: one MartenStoreBackend per tenant, each
        // bound to accessor.Tenant so a write lands in that tenant's own database.
        return builder.FromStore((sp, accessor) =>
            new MartenStoreBackend(sp.GetRequiredService<IDocumentStore>(), accessor.Tenant));
    }
}
