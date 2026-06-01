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
/// All writes are sparse (only the touched leaf is persisted). PatchAsync applies any number
/// of mutations under one atomic read-transform-write — one write, one recompute — and the single-value
/// shorthands (SetAsync etc.) delegate to it.
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

    // ---------------------------------------------------------------- single-value shorthands

    public Task SetAsync<TValue>(Expression<Func<T, TValue>> selector, TValue value, CancellationToken ct = default)
        => PatchAsync(b => b.Set(selector, value), ct);

    public Task SetSecretAsync<TSecret>(Expression<Func<T, ISecret<TSecret>>> selector, SecretEnvelope<TSecret> envelope, CancellationToken ct = default)
        => PatchAsync(b => b.SetSecret(selector, envelope), ct);

    public Task<bool> ResetAsync<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default)
    {
        // Resetting a secret member is safe — it only removes the overlay key (no plaintext is written).
        var keyPath = OverlayPathResolver.ResolveKeyPath(selector, allowSecretMembers: true);
        return ResetAsync(keyPath, ct);
    }

    // ---------------------------------------------------------------- batch patch

    public Task PatchAsync(Action<IWritableStorePatch<T>> configure, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new StorePatchBuilder<T>();
        configure(builder);
        return CommitAsync(builder, ct);
    }

    public async Task PatchAsync(Func<IWritableStorePatch<T>, Task> configureAsync, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configureAsync);
        var builder = new StorePatchBuilder<T>();
        await configureAsync(builder).ConfigureAwait(false);
        await CommitAsync(builder, ct).ConfigureAwait(false);
    }

    private Task CommitAsync(StorePatchBuilder<T> builder, CancellationToken ct)
    {
        // Resolve typed selectors outside the lock (reflection / expression walking).
        var resolved = ResolveMutations(builder);
        if (resolved.Count == 0)
            return Task.CompletedTask;

        return _store.UpdateBytesAsync(currentBytes =>
        {
            // Parse the overlay once, apply every mutation in-memory, serialize once.
            var root = SparseOverlayMutator.Parse(currentBytes);
            foreach (var op in resolved)
            {
                if (op.IsReset)
                    SparseOverlayMutator.Remove(root, op.KeyPath);
                else
                    SparseOverlayMutator.Set(root, op.KeyPath, op.Value);
            }
            return MutableJsonDocument.ToUtf8Bytes(root);
        }, ct);
    }

    // ---------------------------------------------------------------- raw overlay surface

    public async Task SetAsync(string keyPath, JsonNode? value, CancellationToken ct = default)
    {
        ValidateKeyPath(keyPath);
        await _store.UpdateBytesAsync(bytes => SparseOverlayMutator.Set(bytes, keyPath, value), ct)
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

        await _store.UpdateBytesAsync(bytes => SparseOverlayMutator.Set(bytes, keyPath, envelope), ct)
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

    public async Task<T?> ReadAsync(CancellationToken ct = default)
    {
        var bytes = await _store.ReadBytesAsync(ct).ConfigureAwait(false);
        if (bytes.Length <= 2)
            return null;
        return JsonSerializer.Deserialize<T>(bytes, OverlaySerialization.ReadOptions);
    }

    public async Task<IReadOnlyList<StoreEntry>> DescribeAsync(CancellationToken ct = default)
    {
        var baseElement = ToJsonElement(_host.BuildBaseJson(typeof(T), IsThisLayer));
        var effective = _host.GetConfigAsJson(typeof(T));
        var overlayNode = await ReadOverlayAsync(ct).ConfigureAwait(false);

        // Case-insensitive: the pipeline merge is case-insensitive, so the overlay may store a key in a
        // different casing than the base/effective. Treat them as the same provenance entry.
        var overriddenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (overlayNode is JsonObject overlayObject)
            CollectOverlayPaths(overlayObject, null, overriddenPaths);

        var allPaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectLeafPaths(baseElement, null, allPaths);
        if (effective is { } effectiveElement)
            CollectLeafPaths(effectiveElement, null, allPaths);
        allPaths.UnionWith(overriddenPaths);

        var entries = new List<StoreEntry>(allPaths.Count);
        foreach (var path in allPaths)
        {
            JsonElement? baseValue = TrySelect(baseElement, path, out var bv) ? bv : null;
            JsonElement? effectiveValue = effective is { } e && TrySelect(e, path, out var ev) ? ev : null;
            entries.Add(new StoreEntry(path, baseValue, effectiveValue, overriddenPaths.Contains(path)));
        }

        return entries;
    }

    public async Task<JsonNode?> ReadOverlayAsync(CancellationToken ct = default)
    {
        var bytes = await _store.ReadBytesAsync(ct).ConfigureAwait(false);
        return bytes.Length <= 2 ? null : JsonNode.Parse(bytes);
    }

    public void Dispose() => _store.Dispose();

    // ---------------------------------------------------------------- mutation resolution

    private List<ResolvedPatchOperation> ResolveMutations(StorePatchBuilder<T> builder)
    {
        var ops = new List<ResolvedPatchOperation>(builder.Mutations.Count + 8);

        foreach (var mutation in builder.Mutations)
        {
            switch (mutation)
            {
                case TypedSetMutation set:
                {
                    var keyPath = OverlayPathResolver.ResolveKeyPath(set.Selector, typeof(T), allowSecretMembers: false);
                    var node = OverlaySerialization.SerializeValue(set.Value, set.ValueType);
                    ops.Add(new ResolvedPatchOperation(keyPath, node, IsReset: false));
                    break;
                }
                case TypedSecretMutation secret:
                {
                    var keyPath = OverlayPathResolver.ResolveKeyPath(secret.Selector, typeof(T), allowSecretMembers: true);
                    var node = JsonSerializer.SerializeToNode(secret.Envelope)!;
                    ops.Add(new ResolvedPatchOperation(keyPath, node, IsReset: false));
                    break;
                }
                case TypedResetMutation reset:
                {
                    // Resetting a secret member is safe — only the overlay key is removed.
                    var keyPath = OverlayPathResolver.ResolveKeyPath(reset.Selector, typeof(T), allowSecretMembers: true);
                    ops.Add(new ResolvedPatchOperation(keyPath, Value: null, IsReset: true));
                    break;
                }
            }
        }

        return ops;
    }

    // ---------------------------------------------------------------- helpers

    private bool IsThisLayer(IRuleManager manager)
        => manager.CurrentProvider is WritableStoreProvider provider && ReferenceEquals(provider.Store, _store);

    private static JsonElement ToJsonElement(MutableJsonObject obj)
    {
        var bytes = MutableJsonDocument.ToUtf8Bytes(obj);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    private static void ValidateKeyPath(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new ArgumentException("Key path must be a non-empty, dotted property path.", nameof(keyPath));
        foreach (var segment in keyPath.Split('.'))
        {
            if (string.IsNullOrWhiteSpace(segment))
                throw new ArgumentException($"Key path '{keyPath}' contains an empty segment.", nameof(keyPath));
        }
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
        if (prefix is not null)
            paths.Add(prefix);
    }

    private static void CollectOverlayPaths(JsonObject node, string? prefix, ISet<string> paths)
    {
        foreach (var (key, value) in node)
        {
            var childPath = prefix is null ? key : $"{prefix}.{key}";
            if (value is JsonObject childObject)
                CollectOverlayPaths(childObject, childPath, paths);
            else
                paths.Add(childPath);
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

/// <summary>A resolved, key-path-based operation ready for <see cref="SparseOverlayMutator"/>.</summary>
internal sealed record ResolvedPatchOperation(string KeyPath, JsonNode? Value, bool IsReset);
