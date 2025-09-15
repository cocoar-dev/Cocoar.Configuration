// Cocoar.Configuration.Json.JsonPath.cs

using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Cocoar.Configuration.Json;

internal static class JsonPath
{
    private static readonly JsonDocument s_emptyDoc = JsonDocument.Parse("{}");
    internal static readonly JsonElement EmptyObject = s_emptyDoc.RootElement;

    /// <summary>
    /// Wraps element under nested objects for a colon-separated path.
    /// "a:b:c" -> { "a": { "b": { "c": element } } }
    /// </summary>
    public static JsonElement WrapIfNeeded(JsonElement element, string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return element;

        var segments = targetPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return element;

        JsonElement current = element;

        // Build from the leaf outward: {"c": element} -> {"b": {...}} -> {"a": {...}}
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var obj = new Dictionary<string, JsonElement>(1) { [segments[i]] = current };
            current = JsonSerializer.SerializeToElement(obj);
        }

        return current; // detached & safe
    }

    public static bool TrySelectByPath(JsonElement root, string path, out JsonElement result)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            result = root;
            return true;
        }

        var cur = root;
        var span = path.AsSpan();
        var start = 0;

        while (start <= span.Length)
        {
            var rel = span[start..].IndexOf(':');
            var seg = (rel < 0 ? span[start..] : span.Slice(start, rel)).Trim();

            if (!seg.IsEmpty)
            {
                if (cur.ValueKind == JsonValueKind.Array && TryParseNonNegativeInt(seg, out var idx))
                {
                    if ((uint)idx >= (uint)cur.GetArrayLength())
                    {
                        result = default;
                        return false;
                    }

                    cur = cur[idx];
                }
                else if (!TryGetPropertyUtf8(cur, seg, out var next))
                {
                    result = default;
                    return false;
                }
                else cur = next;
            }

            if (rel < 0) break;
            start += rel + 1;
        }

        result = cur;
        return true;
    }

    public static JsonElement SelectByPathOrEmpty(JsonElement root, string path)
        => TrySelectByPath(root, path, out var found) ? found : EmptyObject;

    /// <summary>
    /// Public helper used by pipeline to select a colon-delimited path; throws if not found.
    /// </summary>
    public static JsonElement SelectColonDelimited(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return root;
        if (!TrySelectByPath(root, path, out var found))
            throw new KeyNotFoundException($"Path '{path}' not found in JSON document.");
        return found;
    }

    // --- helpers ---
    private static bool TryParseNonNegativeInt(ReadOnlySpan<char> s, out int value)
    {
        var v = 0;
        if (s.IsEmpty)
        {
            value = 0;
            return false;
        }

        foreach (var c in s)
        {
            if ((uint)(c - '0') > 9u)
            {
                value = 0;
                return false;
            }

            v = v * 10 + (c - '0');
        }

        value = v;
        return true;
    }

    private static bool TryGetPropertyUtf8(JsonElement obj, ReadOnlySpan<char> name, out JsonElement value)
    {
        const int stackLimit = 256;
        var maxBytes = Encoding.UTF8.GetMaxByteCount(name.Length);

        if (maxBytes <= stackLimit)
        {
            Span<byte> buf = stackalloc byte[stackLimit];
            var written = Encoding.UTF8.GetBytes(name, buf);
            return obj.TryGetProperty(buf.Slice(0, written), out value);
        }
        else
        {
            var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
            try
            {
                var written = Encoding.UTF8.GetBytes(name, rented);
                return obj.TryGetProperty(new ReadOnlySpan<byte>(rented, 0, written), out value);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}

// Optional ergonomics: extension methods

internal static class JsonElementExtensions
{
    public static bool TrySelectByPath(this JsonElement root, string path, out JsonElement value)
        => JsonPath.TrySelectByPath(root, path, out value);

    public static JsonElement SelectByPathOrEmpty(this JsonElement root, string path)
        => JsonPath.SelectByPathOrEmpty(root, path);

    public static JsonElement WrapIfNeeded(this JsonElement element, string? path)
        => JsonPath.WrapIfNeeded(element, path);
}
