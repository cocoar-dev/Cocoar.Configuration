using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class StaticJsonProvider(StaticJsonProviderOptions options)
    : ConfigurationProvider<StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(options)
{
    public override Task<JsonElement> FetchConfigurationAsync(StaticJsonProviderQueryOptions query,
        CancellationToken ct = default)
    {
        var json = ProviderOptions.Value.ValueKind == JsonValueKind.Undefined
            ? JsonDocument.Parse("{}").RootElement
            : ProviderOptions.Value;
        return Task.FromResult(json);
    }

    public override IObservable<JsonElement> Changes(StaticJsonProviderQueryOptions queryOptions)
        => Observable.Empty<JsonElement>();
}
