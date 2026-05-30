using System.Text;
using System.Text.Json.Nodes;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Applies sparse mutations to the persisted overlay bytes: setting a single leaf (creating intermediate
/// objects on demand) and removing a leaf (pruning emptied ancestors). Key casing is aligned to the existing
/// overlay first, then to the base layers (Trap B), so an override lands byte-identically on the lower-layer
/// key and acts as an override rather than a sibling.
/// </summary>
internal static class SparseOverlayMutator
{
    /// <summary>
    /// Returns new overlay bytes with <paramref name="valueNode"/> set at <paramref name="keyPath"/>.
    /// A <see langword="null"/> <paramref name="valueNode"/> writes an explicit JSON-null leaf.
    /// </summary>
    internal static byte[] Set(byte[] currentBytes, string keyPath, JsonNode? valueNode, MutableJsonObject? baseDom)
    {
        var root = MutableJsonDocument.Parse(currentBytes) as MutableJsonObject ?? new MutableJsonObject();
        var segments = keyPath.Split('.');

        var parent = root;
        var baseObj = baseDom;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var name = ResolveName(parent, baseObj, segments[i]);

            if (TryGetChild(parent, name) is not MutableJsonObject child)
            {
                child = new MutableJsonObject();
                parent.Set(name, child);
            }

            parent = child;
            // Descend the base by the BASE's own key for this segment (case-insensitive), independent of the
            // name chosen for the overlay — so leaf casing keeps aligning to the base even if an intermediate
            // overlay key has drifted from the base's casing (Trap B, resolve against the base position).
            baseObj = DescendBase(baseObj, segments[i]);
        }

        var leafName = ResolveName(parent, baseObj, segments[^1]);
        parent.Set(leafName, ToMutable(valueNode));

        return MutableJsonDocument.ToUtf8Bytes(root);
    }

    /// <summary>
    /// Returns new overlay bytes with the leaf at <paramref name="keyPath"/> removed and any ancestors that
    /// became empty pruned. The boolean indicates whether anything was removed (idempotent no-op if absent).
    /// </summary>
    internal static (byte[] Bytes, bool Removed) Remove(byte[] currentBytes, string keyPath)
    {
        if (MutableJsonDocument.Parse(currentBytes) is not MutableJsonObject root)
        {
            return (currentBytes, false);
        }

        var segments = keyPath.Split('.');
        var chain = new List<(MutableJsonObject Parent, string Name)>(segments.Length - 1);
        var parent = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var name = FindExistingName(parent, segments[i]);
            if (name is null || TryGetChild(parent, name) is not MutableJsonObject child)
            {
                return (currentBytes, false); // path not present → not overridden
            }

            chain.Add((parent, name));
            parent = child;
        }

        var leafName = FindExistingName(parent, segments[^1]);
        if (leafName is null)
        {
            return (currentBytes, false);
        }

        parent.Remove(leafName);

        // Prune empty ancestors bottom-up so ReadOverlay / provenance stays clean.
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var (ancestor, name) = chain[i];
            if (TryGetChild(ancestor, name) is MutableJsonObject obj && obj.Properties.Count == 0)
            {
                ancestor.Remove(name);
            }
            else
            {
                break;
            }
        }

        return (MutableJsonDocument.ToUtf8Bytes(root), true);
    }

    private static MutableJsonNode ToMutable(JsonNode? node)
        => node is null
            ? MutableJsonNull.Instance
            : MutableJsonDocument.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()));

    /// <summary>
    /// Resolves the exact key name to write: reuse an existing overlay key (avoid casing-variant siblings),
    /// else reuse the base layer's key casing (Trap B), else fall back to the supplied default.
    /// </summary>
    private static string ResolveName(MutableJsonObject overlayParent, MutableJsonObject? baseObj, string defaultName)
        => FindExistingName(overlayParent, defaultName)
           ?? (baseObj is null ? null : FindExistingName(baseObj, defaultName))
           ?? defaultName;

    private static MutableJsonObject? DescendBase(MutableJsonObject? baseObj, string segment)
    {
        if (baseObj is null)
        {
            return null;
        }

        var baseName = FindExistingName(baseObj, segment);
        return baseName is not null ? TryGetChild(baseObj, baseName) as MutableJsonObject : null;
    }

    private static string? FindExistingName(MutableJsonObject obj, string name)
    {
        foreach (var property in obj.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Name;
            }
        }

        return null;
    }

    private static MutableJsonNode? TryGetChild(MutableJsonObject obj, string exactName)
    {
        foreach (var property in obj.Properties)
        {
            if (string.Equals(property.Name, exactName, StringComparison.Ordinal))
            {
                return property.Value;
            }
        }

        return null;
    }
}
