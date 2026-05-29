using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cocoar.Configuration.LocalStorage;

/// <summary>
/// Type-safe facade for a LocalStorage override layer over configuration type <typeparamref name="T"/>.
/// <para>
/// LocalStorage supplies <em>overridable defaults</em>: the normal sources (files, environment, …) provide
/// defaults, and the application overrides individual values at runtime. Writes are <em>sparse</em> — only
/// the keys you set are persisted, everything else continues to inherit from the lower layers. A write
/// triggers the normal recompute, so <c>IReactiveConfig&lt;T&gt;</c> emits the new effective value.
/// </para>
/// <para>
/// Secret-typed members (<c>Secret&lt;T&gt;</c> / <c>ISecret&lt;T&gt;</c>) cannot be overridden through this
/// API and throw <see cref="NotSupportedException"/>; manage secrets via the Secrets CLI/provider.
/// </para>
/// </summary>
/// <typeparam name="T">The configuration type this overlay targets.</typeparam>
public interface ILocalStorage<T> where T : class
{
    /// <summary>
    /// Overrides a single value selected by a member-access expression (e.g. <c>x => x.Smtp.Port</c>),
    /// persisting only that leaf. The member chain is translated into a dotted key path and the value is
    /// serialized with vanilla options (enums as strings); the lower layers are not otherwise touched.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The selector is not a simple member-access chain (e.g. it contains an indexer, method call, or cast),
    /// or it targets a secret-typed member.
    /// </exception>
    Task SetAsync<TValue>(Expression<Func<T, TValue>> selector, TValue value, CancellationToken ct = default);

    /// <summary>
    /// Sets a <em>pre-encrypted</em> secret envelope for a secret-typed member (e.g. <c>x => x.ApiKey</c>),
    /// where the value was encrypted client-side with the server's public certificate so plaintext never
    /// reaches the server. The envelope must be a well-formed <c>cocoar.secret</c> envelope; the normal
    /// <see cref="SetAsync{TValue}"/> still rejects secret members to prevent storing plaintext.
    /// </summary>
    /// <exception cref="ArgumentException">The envelope is not a well-formed encrypted secret envelope.</exception>
    Task SetSecretAsync<TValue>(Expression<Func<T, TValue>> selector, JsonNode envelope, CancellationToken ct = default);

    /// <summary>
    /// Resets a single value to its inherited (lower-layer) value by removing that leaf from the overlay.
    /// </summary>
    /// <returns><see langword="true"/> if an override was removed; <see langword="false"/> if none existed.</returns>
    Task<bool> ResetAsync<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default);

    /// <summary>
    /// Resets everything this layer overrides, so all keys inherit from the lower layers again.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the sparse overlay into a partial <typeparamref name="T"/> where unset properties take their C#
    /// defaults. Returns <see langword="null"/> when nothing is overridden. For the merged/effective value,
    /// use <c>IReactiveConfig&lt;T&gt;.CurrentValue</c> or <c>IConfigurationAccessor.GetConfig&lt;T&gt;()</c>.
    /// </summary>
    Task<T?> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns per-key provenance for a management UI: the union of leaf paths from the base (lower layers,
    /// without this overlay) and the overlay, each annotated with its base value, effective value, and whether
    /// it is currently overridden.
    /// </summary>
    Task<IReadOnlyList<OverrideEntry>> DescribeAsync(CancellationToken ct = default);

    /// <summary>
    /// The raw, key-path overlay surface for dynamic or non-expressible paths.
    /// </summary>
    ILocalStorageOverlay<T> Overlay { get; }
}

/// <summary>
/// Per-leaf provenance entry produced by <see cref="ILocalStorage{T}.DescribeAsync"/>.
/// </summary>
/// <param name="KeyPath">Dotted leaf path (e.g. <c>"Smtp.Port"</c>).</param>
/// <param name="BaseValue">The value from the lower layers, without this overlay; <see langword="null"/> if absent there.</param>
/// <param name="EffectiveValue">The merged/effective value seen by the application; <see langword="null"/> if absent.</param>
/// <param name="IsOverridden"><see langword="true"/> when the overlay supplies this key.</param>
public sealed record OverrideEntry(
    string KeyPath,
    JsonElement? BaseValue,
    JsonElement? EffectiveValue,
    bool IsOverridden);
