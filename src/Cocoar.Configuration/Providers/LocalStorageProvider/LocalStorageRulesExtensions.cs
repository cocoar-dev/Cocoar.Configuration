using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class LocalStorageRulesExtensions
{
    /// <summary>
    /// Creates a local-storage-backed configuration rule.
    /// Reads from and writes to persistent storage. By default uses file-based storage
    /// at <c>{AppContext.BaseDirectory}/.cocoar/localStorage/</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <see cref="Cocoar.Configuration.LocalStorage.ILocalStorage{T}"/> (via DI) to write configuration at runtime.
    /// Writes trigger a recompute of the configuration pipeline.
    /// </para>
    /// <para>
    /// Position this rule in the pipeline to control priority: later rules override earlier ones.
    /// </para>
    /// </remarks>
    /// <param name="builder">The typed provider builder.</param>
    /// <param name="backend">Optional custom storage backend. Defaults to <see cref="FileStorageBackend"/>.</param>
    public static ProviderRuleBuilder<LocalStorageProvider, LocalStorageProviderOptions, LocalStorageProviderQueryOptions>
        FromLocalStorage<T>(this TypedProviderBuilder<T> builder, IStorageBackend? backend = null)
        where T : class
    {
        var effectiveBackend = backend ?? new FileStorageBackend();
        var storageKey = typeof(T).FullName ?? typeof(T).Name;
        var store = new LocalStorageStore(effectiveBackend, storageKey)
        {
            ConfigurationType = typeof(T)
        };

        return new(
            _ => new LocalStorageProviderOptions(store),
            _ => LocalStorageProviderQueryOptions.Default,
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
    /// and the current <see cref="IStorageBackend"/> (null on first call), and returns the backend to use.</param>
    public static ProviderRuleBuilder<LocalStorageProvider, LocalStorageProviderOptions, LocalStorageProviderQueryOptions>
        FromLocalStorage<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, IStorageBackend?, IStorageBackend> backendFactory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(backendFactory);

        LocalStorageStore? store = null;
        var storageKey = typeof(T).FullName ?? typeof(T).Name;

        return new(
            accessor =>
            {
                var currentBackend = store?.Backend;
                var backend = backendFactory(accessor, currentBackend);
                if (store is null)
                {
                    store = new LocalStorageStore(backend, storageKey)
                    {
                        ConfigurationType = typeof(T)
                    };
                }
                else if (!ReferenceEquals(backend, currentBackend))
                {
                    store.ReplaceBackend(backend);
                }
                return new LocalStorageProviderOptions(store);
            },
            _ => LocalStorageProviderQueryOptions.Default,
            typeof(T)
        );
    }
}
