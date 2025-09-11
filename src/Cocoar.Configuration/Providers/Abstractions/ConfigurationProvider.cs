using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigurationProvider
{
    public abstract Task<JsonElement> FetchConfigurationAsync(IProviderQuery? query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(IProviderQuery? query);
    /// <summary>
    /// Wraps a JsonElement in a nested structure if TargetPath (colon-separated) is provided.
    /// </summary>
    protected static JsonElement WrapIfNeeded(JsonElement element, string? TargetPath)
    {
        if (string.IsNullOrWhiteSpace(TargetPath))
            return element;
        object current = element;
        foreach (var seg in TargetPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            current = new Dictionary<string, object?> { [seg] = current };
        }
        var json = JsonSerializer.Serialize(current);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Wraps a Dictionary in a nested structure if TargetPath (colon-separated) is provided.
    /// </summary>
    protected static JsonElement WrapIfNeeded(Dictionary<string, object?> dict, string? TargetPath)
    {
        object result = dict;
        if (!string.IsNullOrWhiteSpace(TargetPath))
        {
            object current = dict;
            foreach (var seg in TargetPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
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
