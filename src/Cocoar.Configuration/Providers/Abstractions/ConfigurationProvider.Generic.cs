using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigurationProvider<TProviderConfiguration, TProviderQuery>(TProviderConfiguration options)
    : ConfigurationProvider
    where TProviderConfiguration : IProviderConfiguration
{
    protected TProviderConfiguration ProviderOptions { get; } = options;

    /// <summary>
    /// Fetches configuration as raw UTF-8 JSON bytes for the typed query.
    /// Override this to provide your provider's implementation.
    /// </summary>
    public abstract Task<byte[]> FetchConfigurationBytesAsync(TProviderQuery query, CancellationToken ct = default);
    
    /// <summary>
    /// Observes configuration changes as raw UTF-8 JSON bytes for the typed query.
    /// Override this to provide your provider's implementation.
    /// </summary>
    public abstract IObservable<byte[]> ChangesAsBytes(TProviderQuery query);

    public override Task<byte[]> FetchConfigurationBytesAsync(IProviderQuery query, CancellationToken ct = default)
    {
        if (query is not TProviderQuery typedQuery)
        {
            throw new ArgumentException($"Expected query of type {typeof(TProviderQuery).FullName}, but received {query.GetType().FullName}", nameof(query));
        }

        return FetchConfigurationBytesAsync(typedQuery, ct);
    }

    public override IObservable<byte[]> ChangesAsBytes(IProviderQuery query)
    {
        if (query is not TProviderQuery typedQuery)
        {
            throw new ArgumentException($"Expected query of type {typeof(TProviderQuery).FullName}, but received {query.GetType().FullName}", nameof(query));
        }

        return ChangesAsBytes(typedQuery);
    }
}
