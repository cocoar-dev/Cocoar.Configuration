using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Service-backed (Layer-2, ADR-006) rule factories that need the application <see cref="IServiceProvider"/>.
/// Valid only inside <c>UseServiceBackedConfiguration(...)</c>.
/// </summary>
public static class ServiceBackedRulesExtensions
{
    /// <summary>
    /// Creates a <b>service-backed</b> storage rule whose <see cref="IStoreBackend"/> is built from the
    /// application container — e.g. a Marten/EF backend over a DI-managed <c>IDocumentStore</c> /
    /// <c>IDbContextFactory&lt;T&gt;</c>. Reuses the (tenant-keyed) WritableStore backend pipeline, so it also
    /// exposes the <c>IWritableStore&lt;T&gt;</c> write facade and composes with <c>.TenantScoped()</c> for
    /// per-tenant, DB-backed configuration.
    /// </summary>
    /// <remarks>
    /// Only valid inside <c>UseServiceBackedConfiguration(...)</c>. The rule stays dormant until the host
    /// starts; the factory is invoked at recompute time, never before the container exists. The
    /// <see cref="IServiceProvider"/> is the ROOT provider: resolve singletons/factories and open short-lived
    /// units per read (<c>store.QuerySession(a.Tenant)</c>), never a scoped service (ADR-006 §9).
    /// </remarks>
    /// <param name="builder">The typed provider builder.</param>
    /// <param name="backendFactory">Factory receiving the root <see cref="IServiceProvider"/> and the current
    /// <see cref="IConfigurationAccessor"/> (its <c>Tenant</c> is set in a tenant pipeline) and returning the
    /// <see cref="IStoreBackend"/> to read configuration from.</param>
    public static ProviderRuleBuilder<WritableStoreProvider, WritableStoreProviderOptions, WritableStoreProviderQueryOptions>
        FromStore<T>(
            this ServiceBackedProviderBuilder<T> builder,
            Func<IServiceProvider, IConfigurationAccessor, IStoreBackend> backendFactory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(backendFactory);

        var context = builder.Context;

        // Reuse the core WritableStore backend-factory rule (already tenant-keyed: one store per tenant, the
        // backend swapped per recompute). currentBackend is ignored — a backend wrapping a DI singleton is cheap
        // to re-create and opens its short-lived units per read.
        var rule = builder.FromStore(
            (accessor, _) => backendFactory(context.ServiceProvider, accessor));

        // sp-gate: dormant until the container is built; composes (AND) with any .TenantScoped() the caller adds,
        // so Marten-per-tenant runs only inside a tenant pipeline post-container.
        return rule.WithActivationGate(_ => context.IsActive);
    }
}
