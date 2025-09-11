using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigurationProvider<TProviderConfiguration, TProviderQuery>(TProviderConfiguration options)
    : ConfigurationProvider
    where TProviderConfiguration : IProviderConfiguration
{
    protected TProviderConfiguration ProviderOptions { get; } = options;

    public abstract Task<JsonElement> FetchConfigurationAsync(TProviderQuery query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(TProviderQuery query);

    public override Task<JsonElement> FetchConfigurationAsync(IProviderQuery? query, CancellationToken ct = default)
        => FetchConfigurationAsync((TProviderQuery)query!, ct);

    public override IObservable<JsonElement> Changes(IProviderQuery? query)
        => Changes((TProviderQuery)query!);
}
