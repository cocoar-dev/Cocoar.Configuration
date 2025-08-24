using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigSourceProvider
{
    public abstract Task<JsonElement> GetValueAsync(ISourceProviderQueryOptions? query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(ISourceProviderQueryOptions? query);
    /// <summary>
    /// Wraps a JsonElement in a nested structure if memberWrapper is provided.
    /// </summary>
    protected static JsonElement WrapIfNeeded(JsonElement element, string? memberWrapper)
    {
        if (string.IsNullOrWhiteSpace(memberWrapper))
            return element;
        var dict = new Dictionary<string, JsonElement?> { [memberWrapper] = element };
        var json = JsonSerializer.Serialize(dict);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Wraps a Dictionary in a nested structure if memberWrapper is provided.
    /// </summary>
    protected static JsonElement WrapIfNeeded(Dictionary<string, object?> dict, string? memberWrapper)
    {
        object result = dict;
        if (!string.IsNullOrWhiteSpace(memberWrapper))
            result = new Dictionary<string, object?> { [memberWrapper] = dict };
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
