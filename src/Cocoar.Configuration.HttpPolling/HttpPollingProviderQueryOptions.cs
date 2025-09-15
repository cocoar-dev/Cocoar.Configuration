using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProviderQueryOptions : IProviderQuery
{
    public string UrlPathOrAbsolute { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingProviderQueryOptions(
        string urlPathOrAbsolute,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        UrlPathOrAbsolute = urlPathOrAbsolute ?? throw new ArgumentNullException(nameof(urlPathOrAbsolute));
        Headers = headers;
    }
}
