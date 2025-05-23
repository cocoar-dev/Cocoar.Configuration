using System.Text.Json;

namespace Cocoar.Configuration.Extensions;

public abstract class ConfigSourceProvider
{
    public abstract Task<JsonElement?> GetValueAsync(ISourceProviderQueryOptions? query, CancellationToken ct = default);
    public abstract IObservable<ConfigChangeNotification> Changes(ISourceProviderQueryOptions? query);
}

public interface ISourceProviderInstanceOptions
{
    string CalculateKey()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            PropertyNamingPolicy = null,
            WriteIndented = false
        };
        return JsonSerializer.Serialize(this, GetType(), options);
    }
    
}



public abstract class ConfigSourceProvider<TInstanceOptions, TQueryOptions>: ConfigSourceProvider
    where TInstanceOptions : ISourceProviderInstanceOptions
{
    protected TInstanceOptions ProviderOptions { get; }
    protected ConfigSourceProvider(TInstanceOptions options) => ProviderOptions = options;

    public abstract Task<JsonElement?> GetValueAsync(TQueryOptions query, CancellationToken ct = default);
    public abstract IObservable<ConfigChangeNotification> Changes(TQueryOptions query);

    public override Task<JsonElement?> GetValueAsync(ISourceProviderQueryOptions? query, CancellationToken ct = default)
        => GetValueAsync((TQueryOptions)query!, ct);

    public override IObservable<ConfigChangeNotification> Changes(ISourceProviderQueryOptions? query)
        => Changes((TQueryOptions)query!);

}