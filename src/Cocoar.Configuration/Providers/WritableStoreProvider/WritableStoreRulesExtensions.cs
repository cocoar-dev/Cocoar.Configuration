using System.Collections.Concurrent;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class WritableStoreRulesExtensions
{
    /// <summary>
    /// Creates a local-storage-backed configuration rule.
    /// Reads from and writes to persistent storage. By default uses file-based storage
    /// at <c>{AppContext.BaseDirectory}/.cocoar/store/</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <see cref="Cocoar.Configuration.WritableStore.IWritableStore{T}"/> (via DI) to write configuration at runtime.
    /// Writes trigger a recompute of the configuration pipeline.
    /// </para>
    /// <para>
    /// Position this rule in the pipeline to control priority: later rules override earlier ones.
    /// </para>
    /// </remarks>
    /// <param name="builder">The typed provider builder.</param>
    /// <param name="backend">Optional custom storage backend. Defaults to <see cref="FileStoreBackend"/>.</param>
    public static ProviderRuleBuilder<WritableStoreProvider, WritableStoreProviderOptions, WritableStoreProviderQueryOptions>
        FromStore<T>(this TypedProviderBuilder<T> builder, IStoreBackend? backend = null)
        where T : class
    {
        var effectiveBackend = backend ?? new FileStoreBackend();
        var storageKey = typeof(T).FullName ?? typeof(T).Name;
        var store = new WritableStoreState(effectiveBackend, storageKey)
        {
            ConfigurationType = typeof(T)
        };

        return new(
            _ => new WritableStoreProviderOptions(store),
            _ => WritableStoreProviderQueryOptions.Default,
            typeof(T)
        );
    }

    /// <summary>
    /// Creates a local-storage-backed configuration rule using a factory that receives the current
    /// configuration state and the current backend. Use this when the storage backend depends on
    /// values from earlier rules (e.g., a connection string for a database-backed backend).
    /// </summary>
    /// <remarks>
    /// The factory is called on every recompute. The second parameter (<c>currentBackend</c>) is
    /// the backend currently in use (<c>null</c> on the first call). Return it unchanged to skip
    /// creating a new instance when nothing relevant changed — this avoids unnecessary
    /// connection pool churn for database backends.
    /// </remarks>
    /// <param name="builder">The typed provider builder.</param>
    /// <param name="backendFactory">A factory that receives the current <see cref="IConfigurationAccessor"/>
    /// and the current <see cref="IStoreBackend"/> (null on first call), and returns the backend to use.</param>
    public static ProviderRuleBuilder<WritableStoreProvider, WritableStoreProviderOptions, WritableStoreProviderQueryOptions>
        FromStore<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, IStoreBackend?, IStoreBackend> backendFactory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(backendFactory);

        // One store per tenant ("" = the global pipeline). The same ConfigRule is shared across the global and
        // every tenant pipeline; keying the store by accessor.Tenant gives each tenant its OWN store+backend, so
        // a per-tenant overlay never aliases another tenant's (ADR-005 §7). A given tenant is driven by a single
        // pipeline whose recomputes are serialized, so each entry is only ever touched by one writer at a time.
        var stores = new ConcurrentDictionary<string, WritableStoreState>();
        var storageKey = typeof(T).FullName ?? typeof(T).Name;

        return new(
            accessor =>
            {
                var tenantKey = accessor.Tenant ?? string.Empty;
                stores.TryGetValue(tenantKey, out var store);
                var currentBackend = store?.Backend;
                var backend = backendFactory(accessor, currentBackend);
                if (store is null)
                {
                    store = new WritableStoreState(backend, storageKey)
                    {
                        ConfigurationType = typeof(T)
                    };
                    stores[tenantKey] = store;
                }
                else if (!ReferenceEquals(backend, currentBackend))
                {
                    store.ReplaceBackend(backend);
                }
                return new WritableStoreProviderOptions(store);
            },
            _ => WritableStoreProviderQueryOptions.Default,
            typeof(T)
        );
    }
}
