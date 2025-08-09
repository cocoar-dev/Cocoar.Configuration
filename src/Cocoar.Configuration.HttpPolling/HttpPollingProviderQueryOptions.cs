using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProviderQueryOptions : ISourceProviderQueryOptions
{
    public string UrlPathOrAbsolute { get; }
    public string? MemberPath { get; }
    public string? MemberWrapper { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingProviderQueryOptions(string urlPathOrAbsolute, string? memberPath = null, string? memberWrapper = null, IReadOnlyDictionary<string,string>? headers = null)
    {
        UrlPathOrAbsolute = urlPathOrAbsolute ?? throw new ArgumentNullException(nameof(urlPathOrAbsolute));
        MemberPath = memberPath;
        MemberWrapper = memberWrapper;
        Headers = headers;
    }
}
