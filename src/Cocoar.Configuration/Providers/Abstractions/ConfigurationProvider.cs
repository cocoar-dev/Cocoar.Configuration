using System.Text.Json;
using Cocoar.Configuration.Helper;

namespace Cocoar.Configuration.Providers.Abstractions;

public abstract class ConfigurationProvider
{
    /// <summary>
    /// Fetches configuration as raw UTF-8 JSON bytes, avoiding string allocations.
    /// This is more secure for sensitive data as bytes can be zeroed after use.
    /// </summary>
    public abstract Task<byte[]> FetchConfigurationBytesAsync(IProviderQuery query, CancellationToken ct = default);

    /// <summary>
    /// Observes configuration changes as raw UTF-8 JSON bytes, avoiding string allocations.
    /// This is more secure for sensitive data as bytes can be zeroed after use.
    /// </summary>
    public abstract IObservable<byte[]> ChangesAsBytes(IProviderQuery query);
}
