using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.WritableStore;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.SecretTypes;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Implements both the type-safe <see cref="IWritableStore{T}"/> facade and the raw
/// <see cref="IWritableStoreOverlay{T}"/> surface over a single <see cref="WritableStoreState"/>.
/// All writes are sparse (only the touched leaf is persisted) and go through the store's atomic
/// read-transform-write lock; provenance is computed from the base layers, the merged effective value,
/// and the persisted overlay.
/// </summary>
internal sealed class WritableStoreAdapter<T> : IWritableStore<T>, IWritableStoreOverlay<T>, IDisposable
    where T : class
{
    private readonly IWritableStoreHost _host;
    private readonly WritableStoreState _store;

    // _host is the pipeline this overlay belongs to: the global ConfigManager, or a TenantPipeline for a
    // per-tenant overlay (ADR-005 §7). Both supply base/effective JSON over their OWN rule managers/snapshot.
    public WritableStoreAdapter(IWritableStoreHost host, WritableStoreState store)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public IWritableStoreOverlay<T> Overlay => this;

    // ---------------------------------------------------------------- typed facade

    public Task SetAsync<TValue>(Expression<Func<T, TValue>> selector, TValue value, CancellationToken ct = default)
    {
        if (OverlayPathResolver.ContainsSecret(typeof(TValue)))
        {
            throw new NotSupportedException(
                $"Cannot store a value of type '{typeof(TValue).Name}' via SetAsync because it is, or contains, a secret. " +
                "A secret would be serialized as plaintext (or lost). Set secret members individually via " +
                "SetSecretAsync with a pre-encrypted SecretEnvelope.");
        }

        var keyPath = OverlayPathResolver.ResolveKeyPath(selector);
        var node = OverlaySerialization.SerializeValue(value);
        return SetAsync(keyPath, node, ct);
    }

    public Task<bool> ResetAsync<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default)
    {
        var keyPath = OverlayPathResolver.ResolveKeyPath(selector);
        return ResetAsync(keyPath, ct);
    }

    public Task SetSecretAsync<TSecret>(Expression<Func<T, ISecret<TSecret>>> selector, SecretEnvelope<TSecret> envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        // Secret members are allowed here ONLY because the value is a pre-encrypted envelope (validated below).
        var keyPath = OverlayPathResolver.ResolveKeyPath(selector, allowSecretMembers: true);
        var node = JsonSerializer.SerializeToNode(envelope)!;
        return SetSecretEnvelopeAsync(keyPath, node, ct);
    }

    public async Task<T?> ReadAsync(CancellationToken ct = default)
    {
        var bytes = await _store.ReadBytesAsync(ct).ConfigureAwait(false);
        if (bytes.Length <= 2)
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(bytes, OverlaySerialization.ReadOptions);
    }

    public async Task<IReadOnlyList<StoreEntry>> DescribeAsync(CancellationToken ct = default)
    {
        var baseElement = ToJsonElement(_host.BuildBaseJson(typeof(T), IsThisLayer));
        var effective = _host.GetConfigAsJson(typeof(T));
        var overlayNode = await ReadOverlayAsync(ct).ConfigureAwait(false);

        var overriddenPaths = new HashSet<string>(StringComparer.Ordinal);
        if (overlayNode is JsonObject overlayObject)
        {
            CollectOverlayPaths(overlayObject, null, overriddenPaths);
        }

        var allPaths = new SortedSet<string>(StringComparer.Ordinal);
        CollectLeafPaths(baseElement, null, allPaths);
        if (effective is { } effectiveElement)
        {
            CollectLeafPaths(effectiveElement, null, allPaths);
        }
        allPaths.UnionWith(overriddenPaths);

        var entries = new List<StoreEntry>(allPaths.Count);
        foreach (var path in allPaths)
        {
            JsonElement? baseValue = TrySelect(baseElement, path, out var bv) ? bv : null;
            JsonElement? effectiveValue =
                effective is { } e && TrySelect(e, path, out var ev) ? ev : null;

            entries.Add(new StoreEntry(path, baseValue, effectiveValue, overriddenPaths.Contains(path)));
        }

        return entries;
    }

    // ---------------------------------------------------------------- raw overlay surface

    public async Task SetAsync(string keyPath, JsonNode? value, CancellationToken ct = default)
    {
        ValidateKeyPath(keyPath);
        var baseDom = _host.BuildBaseJson(typeof(T), IsThisLayer);
        await _store.UpdateBytesAsync(bytes => SparseOverlayMutator.Set(bytes, keyPath, value, baseDom), ct)
            .ConfigureAwait(false);
    }

    public async Task SetSecretEnvelopeAsync(string keyPath, JsonNode envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateKeyPath(keyPath);

        // Only pre-encrypted envelopes are accepted — never plaintext (which would expose the secret) nor the
        // masked "***" form (which would destroy a real secret on the lower layer).
        var element = JsonSerializer.SerializeToElement(envelope);
        if (!SecretEnvelopeWrapper.IsEnvelope(element))
        {
            throw new ArgumentException(
                "Value is not a well-formed encrypted secret envelope (expected an object with " +
                "type=\"cocoar.secret\" and version=1). WritableStore only accepts pre-encrypted secret envelopes.",
                nameof(envelope));
        }

        var baseDom = _host.BuildBaseJson(typeof(T), IsThisLayer);
        await _store.UpdateBytesAsync(bytes => SparseOverlayMutator.Set(bytes, keyPath, envelope, baseDom), ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> ResetAsync(string keyPath, CancellationToken ct = default)
    {
        ValidateKeyPath(keyPath);
        var removed = false;
        await _store.UpdateBytesAsync(bytes =>
        {
            var (updated, didRemove) = SparseOverlayMutator.Remove(bytes, keyPath);
            removed = didRemove;
            return updated;
        }, ct).ConfigureAwait(false);
        return removed;
    }

    public Task ClearAsync(CancellationToken ct = default)
        => _store.WriteBytesAsync("{}"u8.ToArray(), ct);

    public async Task<JsonNode?> ReadOverlayAsync(CancellationToken ct = default)
    {
        var bytes = await _store.ReadBytesAsync(ct).ConfigureAwait(false);
        if (bytes.Length <= 2)
        {
            return null;
        }

        return JsonNode.Parse(bytes);
    }

    public void Dispose() => _store.Dispose();

    // ---------------------------------------------------------------- helpers

    private bool IsThisLayer(IRuleManager manager)
        => manager.CurrentProvider is WritableStoreProvider provider && ReferenceEquals(provider.Store, _store);

    private static void ValidateKeyPath(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            throw new ArgumentException("Key path must be a non-empty, dotted property path.", nameof(keyPath));
        }

        foreach (var segment in keyPath.Split('.'))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                throw new ArgumentException(
                    $"Key path '{keyPath}' contains an empty segment.", nameof(keyPath));
            }
        }
    }

    private static JsonElement ToJsonElement(MutableJsonObject obj)
    {
        var bytes = MutableJsonDocument.ToUtf8Bytes(obj);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    private static void CollectLeafPaths(JsonElement element, string? prefix, ISet<string> paths)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childPath = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                CollectLeafPaths(property.Value, childPath, paths);
            }

            return;
        }

        // Arrays and scalars (and explicit null) are treated as leaves — the merge replaces arrays wholesale.
        if (prefix is not null)
        {
            paths.Add(prefix);
        }
    }

    private static void CollectOverlayPaths(JsonObject node, string? prefix, ISet<string> paths)
    {
        foreach (var (key, value) in node)
        {
            var childPath = prefix is null ? key : $"{prefix}.{key}";
            if (value is JsonObject childObject)
            {
                CollectOverlayPaths(childObject, childPath, paths);
            }
            else
            {
                // A JsonValue, JsonArray, or explicit null (value is null) is an overridden leaf.
                paths.Add(childPath);
            }
        }
    }

    private static bool TrySelect(JsonElement root, string dottedPath, out JsonElement result)
    {
        var current = root;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                result = default;
                return false;
            }

            current = next;
        }

        result = current;
        return true;
    }
}
