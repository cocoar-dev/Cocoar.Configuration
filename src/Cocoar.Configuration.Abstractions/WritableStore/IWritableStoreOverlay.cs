using System.Text.Json.Nodes;

namespace Cocoar.Configuration.WritableStore;

/// <summary>
/// Raw, key-path patch surface for a WritableStore override layer.
/// <para>
/// WritableStore contributes a <em>sparse</em> overlay: only the leaf keys it explicitly contains
/// override the lower configuration layers (files, environment, …). Keys that are absent from the
/// overlay inherit their value from those lower layers. This interface is the dependency-free
/// (<c>JsonNode</c>-based) escape hatch used by the typed <see cref="IWritableStore{T}"/> facade and by
/// callers that need to address dynamic / non-expressible paths.
/// </para>
/// <para>
/// Segments of the key path must match the JSON property names used by the lower layers.
/// The typed facade resolves these automatically; when using this raw surface directly, the caller is
/// responsible for the names. Do not use this surface for secret-typed paths — see <see cref="IWritableStore{T}"/>.
/// </para>
/// </summary>
/// <typeparam name="T">The configuration type this overlay targets.</typeparam>
public interface IWritableStoreOverlay<T> where T : class
{
    /// <summary>
    /// Sets a sparse, dotted key path (e.g. <c>"Smtp.Port"</c>) to a JSON value, persisting only that leaf.
    /// </summary>
    /// <param name="keyPath">Dotted path whose segments match the persisted JSON property names.</param>
    /// <param name="value">
    /// The JSON value to store at the leaf. A <see langword="null"/> reference writes an explicit JSON
    /// <c>null</c> override (clobbers the lower-layer value to <c>null</c>) — this is distinct from
    /// <see cref="ResetAsync"/>, which removes the override entirely.
    /// </param>
    /// <param name="ct">A token to cancel the write.</param>
    Task SetAsync(string keyPath, JsonNode? value, CancellationToken ct = default);

    /// <summary>
    /// Sets a <em>pre-encrypted</em> secret envelope at a dotted key path. The value MUST be a well-formed
    /// <c>cocoar.secret</c> envelope (produced client-side with the server's public certificate, or by the
    /// Secrets CLI); plaintext and the masked <c>"***"</c> form are rejected, so the secret never reaches the
    /// server in the clear.
    /// </summary>
    /// <param name="keyPath">Dotted path to the secret member.</param>
    /// <param name="envelope">A well-formed encrypted secret envelope (object with <c>type</c>=<c>"cocoar.secret"</c>).</param>
    /// <param name="ct">A token to cancel the write.</param>
    /// <exception cref="ArgumentException">The value is not a well-formed encrypted secret envelope.</exception>
    Task SetSecretEnvelopeAsync(string keyPath, JsonNode envelope, CancellationToken ct = default);

    /// <summary>
    /// Removes a key path from the overlay so the value falls back to the lower-layer (inherited) value.
    /// </summary>
    /// <returns><see langword="true"/> if a key was removed; <see langword="false"/> if it was already absent.</returns>
    Task<bool> ResetAsync(string keyPath, CancellationToken ct = default);

    /// <summary>
    /// Clears the entire overlay, persisting an empty object so every key inherits again.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the raw sparse overlay exactly as persisted (the override fragment, NOT the merged result).
    /// Returns <see langword="null"/> when the overlay is empty.
    /// </summary>
    Task<JsonNode?> ReadOverlayAsync(CancellationToken ct = default);
}
