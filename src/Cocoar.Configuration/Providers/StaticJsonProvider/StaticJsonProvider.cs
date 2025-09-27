using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;

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

    public static ConfigRule CreateRule<TConfigType>(JsonElement value, Func<bool>? useWhen = null,
        bool required = false)
    {
        var opts = new ConfigRuleOptions(Required: required, UseWhen: useWhen);
        return ConfigRule.Create<StaticJsonProvider, StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(
            _ => new(value),
            _ => new(),
            typeof(TConfigType),
            opts);
    }

    public static ConfigRule CreateRule<TConfigType>(string jsonString, Func<bool>? useWhen = null,
        bool required = false)
    {
        using var document = JsonDocument.Parse(jsonString);
        var jsonElement = document.RootElement.Clone();
        return CreateRule<TConfigType>(jsonElement, useWhen, required);
    }
}
