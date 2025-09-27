using System.Text.Json;
using Cocoar.Configuration.Helper;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigurationProvider
{
    public abstract Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(IProviderQuery query);

}
