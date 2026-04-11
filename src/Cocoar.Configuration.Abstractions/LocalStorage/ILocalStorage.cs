namespace Cocoar.Configuration.LocalStorage;

/// <summary>
/// Provides read and write access to the local storage backing a configuration type.
/// Inject via DI to read or write configuration at runtime.
/// Writing triggers a recompute of the configuration pipeline, updating
/// <c>IReactiveConfig&lt;T&gt;</c> for all consumers.
/// </summary>
/// <typeparam name="T">The configuration type.</typeparam>
public interface ILocalStorage<T> where T : class
{
    /// <summary>
    /// Reads the current value from storage. Returns <c>null</c> if nothing
    /// has been persisted yet.
    /// </summary>
    /// <remarks>
    /// This returns the raw stored value — not the merged pipeline result.
    /// Use <c>IReactiveConfig&lt;T&gt;.CurrentValue</c> for the final merged configuration.
    /// </remarks>
    Task<T?> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Serializes the value to UTF-8 JSON bytes, persists it to storage,
    /// and signals the configuration system to recompute.
    /// </summary>
    Task WriteAsync(T value, CancellationToken ct = default);

    /// <summary>
    /// Atomically reads the current value, applies the update, and writes it back.
    /// The entire operation runs under an exclusive lock — concurrent updates are
    /// serialized and each sees the previous update's result.
    /// </summary>
    /// <remarks>
    /// If nothing has been stored yet, the update receives a default-constructed <typeparamref name="T"/>.
    /// Mutate the properties you want to change; everything else is preserved.
    /// </remarks>
    Task UpdateAsync(Action<T> update, CancellationToken ct = default);
}
