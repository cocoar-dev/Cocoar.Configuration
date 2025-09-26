using System.Text.Json;
using Cocoar.Configuration.Helper;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigurationProvider
{
    public abstract Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(IProviderQuery query);

    /// <summary>
    /// Select a nested property by colon-separated path. Returns empty object if path not found.
    /// </summary>
    protected static JsonElement SelectByPath(JsonElement element, string path)
    {
        return JsonHelper.SelectByPathOrEmpty(element, path);
    }
}
