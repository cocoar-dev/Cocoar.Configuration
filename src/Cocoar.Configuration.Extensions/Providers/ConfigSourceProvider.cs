using System.Text.Json;

namespace Cocoar.Configuration.Extensions.Providers;

public abstract class ConfigSourceProvider
{
    public abstract Task<JsonElement?> GetValueAsync(ISourceProviderQueryOptions? query, CancellationToken ct = default);
    public abstract IObservable<ConfigChangeNotification> Changes(ISourceProviderQueryOptions? query);
}
