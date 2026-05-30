using Cocoar.Configuration.Reactive.Internal;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Shared state object that bridges the provider (read path) and <c>IWritableStore&lt;T&gt;</c> (write path).
/// Created once in <c>FromStore()</c> and captured by both the provider options closure and DI registration.
/// </summary>
public sealed class WritableStoreState : IDisposable
{
    private volatile IStoreBackend _backend;
    private readonly string _storageKey;
    private readonly SimpleSubject<byte[]> _changeSubject = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public WritableStoreState(IStoreBackend backend, string storageKey)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        _backend = backend;
        _storageKey = storageKey;
    }

    /// <summary>
    /// The current storage backend. Exposed so the config-aware factory can
    /// pass it back to the user for comparison/reuse decisions.
    /// </summary>
    internal IStoreBackend Backend => _backend;

    /// <summary>
    /// Replaces the storage backend. Called during recompute when config-aware
    /// factory produces a new backend (e.g., connection string changed).
    /// The store instance stays the same — DI references remain valid.
    /// </summary>
    internal void ReplaceBackend(IStoreBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>
    /// The configuration type this store is associated with.
    /// Used by DI registration to match <c>IWritableStore&lt;T&gt;</c> to the correct store.
    /// </summary>
    public Type ConfigurationType { get; init; } = null!;

    /// <summary>
    /// Observable that fires when new bytes are written. Subscribed to by the provider.
    /// </summary>
    internal IObservable<byte[]> Changes => _changeSubject;

    /// <summary>
    /// Reads current bytes from storage. Returns empty JSON object if nothing persisted yet.
    /// </summary>
    internal async Task<byte[]> ReadBytesAsync(CancellationToken ct = default)
    {
        var data = await _backend.ReadAsync(_storageKey, ct).ConfigureAwait(false);
        return data ?? "{}"u8.ToArray();
    }

    /// <summary>
    /// Writes bytes to storage and signals the change subject.
    /// Thread-safe via SemaphoreSlim.
    /// </summary>
    public async Task WriteBytesAsync(byte[] data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _backend.WriteAsync(_storageKey, data, ct).ConfigureAwait(false);
            _changeSubject.OnNext(data);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Atomically reads current bytes, applies a transform, and writes the result.
    /// The entire read-transform-write cycle runs under the write lock.
    /// </summary>
    internal async Task UpdateBytesAsync(Func<byte[], byte[]> transform, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await _backend.ReadAsync(_storageKey, ct).ConfigureAwait(false)
                          ?? "{}"u8.ToArray();
            var updated = transform(current);
            await _backend.WriteAsync(_storageKey, updated, ct).ConfigureAwait(false);
            _changeSubject.OnNext(updated);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _changeSubject.Dispose();
    }
}
