using System.Text.Json;

namespace Cocoar.Configuration.Extensions.Providers;

public abstract class ConfigSourceProvider<TInstanceOptions, TQueryOptions>(TInstanceOptions options)
    : ConfigSourceProvider
    where TInstanceOptions : ISourceProviderInstanceOptions
{
    protected TInstanceOptions ProviderOptions { get; } = options;

    public abstract Task<JsonElement?> GetValueAsync(TQueryOptions query, CancellationToken ct = default);
    public abstract IObservable<ConfigChangeNotification> Changes(TQueryOptions query);

    public override Task<JsonElement?> GetValueAsync(ISourceProviderQueryOptions? query, CancellationToken ct = default)
        => GetValueAsync((TQueryOptions)query!, ct);

    public override IObservable<ConfigChangeNotification> Changes(ISourceProviderQueryOptions? query)
        => Changes((TQueryOptions)query!);
}
