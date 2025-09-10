using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.StaticJsonProvider;

public sealed class StaticJsonProvider(StaticJsonProviderOptions options)
    : ConfigSourceProvider<StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(options)
{
    public override Task<JsonElement> GetValueAsync(StaticJsonProviderQueryOptions queryOptions, CancellationToken ct = default)
    {
        var json = ProviderOptions.Value.ValueKind == JsonValueKind.Undefined
            ? JsonDocument.Parse("{}").RootElement
            : ProviderOptions.Value;
    return Task.FromResult(WrapIfNeeded(json, queryOptions.WrapperPath));
    }

    public override IObservable<JsonElement> Changes(StaticJsonProviderQueryOptions queryOptions)
        => Observable.Empty<JsonElement>();

    public static ConfigRule CreateRule<TConfigType>(JsonElement value, string? wrapperPath = null, Func<bool>? useWhen = null, bool required = true)
        => ConfigRule.Create<StaticJsonProvider, StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(
            _ => new StaticJsonProviderOptions(value),
            _ => new StaticJsonProviderQueryOptions(wrapperPath),
            new ConfigRegistration(typeof(TConfigType)),
            useWhen,
            required
        );
}
