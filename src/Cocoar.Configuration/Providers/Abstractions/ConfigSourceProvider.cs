using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigSourceProvider
{
    public abstract Task<JsonElement> GetValueAsync(ISourceProviderQueryOptions? query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(ISourceProviderQueryOptions? query);
    /// <summary>
    /// Wraps a JsonElement in a nested structure if wrapperPath (colon-separated) is provided.
    /// </summary>
    protected static JsonElement WrapIfNeeded(JsonElement element, string? wrapperPath)
    {
        if (string.IsNullOrWhiteSpace(wrapperPath))
            return element;
        object current = element;
        foreach (var seg in wrapperPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            current = new Dictionary<string, object?> { [seg] = current };
        }
        var json = JsonSerializer.Serialize(current);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Wraps a Dictionary in a nested structure if wrapperPath (colon-separated) is provided.
    /// </summary>
    protected static JsonElement WrapIfNeeded(Dictionary<string, object?> dict, string? wrapperPath)
    {
        object result = dict;
        if (!string.IsNullOrWhiteSpace(wrapperPath))
        {
            object current = dict;
            foreach (var seg in wrapperPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
            {
                current = new Dictionary<string, object?> { [seg] = current };
            }
            result = current;
        }
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Select a nested property by colon-separated path. Returns empty object if path not found.
    /// </summary>
    protected static JsonElement SelectByPath(JsonElement element, string path)
    {
        var cur = element;
        foreach (var seg in path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (cur.ValueKind == JsonValueKind.Object && cur.TryGetProperty(seg, out var next))
            {
                cur = next;
            }
            else
            {
                using var empty = JsonDocument.Parse("{}");
                return empty.RootElement.Clone();
            }
        }
        return cur;
    }
}
