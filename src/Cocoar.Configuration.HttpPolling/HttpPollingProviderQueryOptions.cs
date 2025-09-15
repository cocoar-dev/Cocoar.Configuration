using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProviderQueryOptions : IProviderQuery
{
    public string UrlPathOrAbsolute { get; }
    // Selecting a property (section) from the fetched JSON payload, optional (colon-separated path)
    public string? ConfigurationPath { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingProviderQueryOptions(
    string urlPathOrAbsolute,
    string? configurationPath = null,
    IReadOnlyDictionary<string,string>? headers = null)
    {
        UrlPathOrAbsolute = urlPathOrAbsolute ?? throw new ArgumentNullException(nameof(urlPathOrAbsolute));
        ConfigurationPath = configurationPath;
        Headers = headers;
    }
}
