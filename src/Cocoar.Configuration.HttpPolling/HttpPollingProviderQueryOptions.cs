using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProviderQueryOptions : ISourceProviderQueryOptions
{
    public string UrlPathOrAbsolute { get; }
    // Selecting a property (section) from the fetched JSON payload, optional
    public string? KeyPrefix { get; }
    // Optionally wrap the resulting object into this path
    public string? WrapperPath { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingProviderQueryOptions(
        string urlPathOrAbsolute,
        string? keyPrefix = null,
        string? wrapperPath = null,
        IReadOnlyDictionary<string,string>? headers = null)
    {
        UrlPathOrAbsolute = urlPathOrAbsolute ?? throw new ArgumentNullException(nameof(urlPathOrAbsolute));
        KeyPrefix = keyPrefix;
        WrapperPath = wrapperPath;
        Headers = headers;
    }
}
