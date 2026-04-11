using System.Text.Json;
using Cocoar.Configuration.LocalStorage;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Adapts the untyped <see cref="LocalStorageStore"/> to the typed <see cref="ILocalStorage{T}"/> interface.
/// Handles serialization from T to UTF-8 JSON bytes.
/// </summary>
public sealed class LocalStorageAdapter<T>(LocalStorageStore store) : ILocalStorage<T> where T : class
{
    public async Task<T?> ReadAsync(CancellationToken ct = default)
    {
        var bytes = await store.ReadBytesAsync(ct).ConfigureAwait(false);

        // ReadBytesAsync returns "{}" when nothing is persisted.
        // Deserializing "{}" gives a default-constructed T, but we want null
        // to clearly signal "nothing stored yet".
        if (bytes.Length <= 2)
            return null;

        return JsonSerializer.Deserialize<T>(bytes);
    }

    public async Task WriteAsync(T value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await store.WriteBytesAsync(bytes, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Action<T> update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await store.UpdateBytesAsync(currentBytes =>
        {
            var current = JsonSerializer.Deserialize<T>(currentBytes) ?? throw new InvalidOperationException(
                $"Failed to deserialize {typeof(T).Name} from stored bytes.");
            update(current);
            return JsonSerializer.SerializeToUtf8Bytes(current);
        }, ct).ConfigureAwait(false);
    }
}
