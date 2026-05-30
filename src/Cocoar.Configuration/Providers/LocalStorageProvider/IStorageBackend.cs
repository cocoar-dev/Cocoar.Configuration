namespace Cocoar.Configuration.Providers;

/// <summary>
/// Abstraction for the persistence layer used by LocalStorageProvider.
/// Default implementation is file-based; can be replaced with SQLite, Marten, etc.
/// </summary>
public interface IStorageBackend
{
    /// <summary>
    /// Reads raw UTF-8 JSON bytes for the given key.
    /// Returns null if no data has been persisted yet.
    /// </summary>
    Task<byte[]?> ReadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Writes raw UTF-8 JSON bytes atomically for the given key.
    /// </summary>
    Task WriteAsync(string key, byte[] data, CancellationToken ct = default);
}
