using System.Text.Json;
using Cocoar.Configuration.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigurationProvider
{
    public abstract Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(IProviderQuery query);


    /// <summary>
    /// Wraps a JsonElement in a nested structure if TargetPath (colon-separated) is provided.
    /// </summary>
    protected static JsonElement WrapIfNeeded(JsonElement element, string? targetPath)
    {
        return JsonPath.WrapIfNeeded(element, targetPath);
    }

    /// <summary>
    /// Select a nested property by colon-separated path. Returns empty object if path not found.
    /// </summary>
    protected static JsonElement SelectByPath(JsonElement element, string path)
    {
        return JsonPath.SelectByPathOrEmpty(element, path);
    }
}
