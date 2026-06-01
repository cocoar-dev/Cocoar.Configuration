using System.Text;
using System.Text.Json.Nodes;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Thin glue between the overlay's persisted bytes / <see cref="JsonNode"/> values and the
/// <see cref="MutableJsonPath"/> primitives: set a sparse leaf (creating intermediate objects) and remove a
/// leaf (pruning emptied ancestors). Keys are matched case-insensitively.
/// <para>
/// Key-casing alignment to the lower layers is intentionally <em>not</em> done here: the pipeline merge is
/// case-insensitive (<see cref="Core.ConfigMergeOptions"/>), so an overlay key overrides the base key
/// regardless of casing — the overlay's own casing no longer matters.
/// </para>
/// </summary>
internal static class SparseOverlayMutator
{
    private static readonly MutableJsonPathOptions SetOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly MutableJsonRemovePathOptions RemoveOptions =
        new() { PropertyNameCaseInsensitive = true, PruneEmptyAncestors = true };

    // ---------------------------------------------------------------- in-memory (batch: parse once, write once)

    internal static void Set(MutableJsonObject root, string keyPath, JsonNode? value)
        => root.SetAtPath(keyPath.Split('.'), ToMutable(value), SetOptions);

    internal static bool Remove(MutableJsonObject root, string keyPath)
        => root.RemoveAtPath(keyPath.Split('.'), RemoveOptions);

    internal static MutableJsonObject Parse(byte[] bytes)
        => MutableJsonDocument.Parse(bytes) as MutableJsonObject ?? new MutableJsonObject();

    // ---------------------------------------------------------------- byte wrappers (single raw-overlay ops)

    internal static byte[] Set(byte[] currentBytes, string keyPath, JsonNode? value)
    {
        var root = Parse(currentBytes);
        Set(root, keyPath, value);
        return MutableJsonDocument.ToUtf8Bytes(root);
    }

    internal static (byte[] Bytes, bool Removed) Remove(byte[] currentBytes, string keyPath)
    {
        if (MutableJsonDocument.Parse(currentBytes) is not MutableJsonObject root)
        {
            return (currentBytes, false);
        }

        return Remove(root, keyPath)
            ? (MutableJsonDocument.ToUtf8Bytes(root), true)
            : (currentBytes, false);
    }

    private static MutableJsonNode ToMutable(JsonNode? node)
        => node is null
            ? MutableJsonNull.Instance
            : MutableJsonDocument.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()));
}
