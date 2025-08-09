using System.Text.Json;

namespace Cocoar.Configuration.Providers;

public abstract class ConfigSourceProvider<TInstanceOptions, TQueryOptions>(TInstanceOptions options)
    : ConfigSourceProvider
    where TInstanceOptions : ISourceProviderInstanceOptions
{
    protected TInstanceOptions ProviderOptions { get; } = options;

    public abstract Task<JsonElement> GetValueAsync(TQueryOptions query, CancellationToken ct = default);
    public abstract IObservable<JsonElement> Changes(TQueryOptions query);

    public override Task<JsonElement> GetValueAsync(ISourceProviderQueryOptions? query, CancellationToken ct = default)
        => GetValueAsync((TQueryOptions)query!, ct);

    public override IObservable<JsonElement> Changes(ISourceProviderQueryOptions? query)
        => Changes((TQueryOptions)query!);
}
