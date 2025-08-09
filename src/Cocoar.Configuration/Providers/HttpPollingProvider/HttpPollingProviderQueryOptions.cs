using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.HttpPollingProvider;

public sealed class HttpPollingProviderQueryOptions : ISourceProviderQueryOptions
{
    public string? MemberPath { get; }
    public string? MemberWrapper { get; }

    // Relative path or full URL if BaseAddress is null
    public string UrlPathOrAbsolute { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingProviderQueryOptions(string urlPathOrAbsolute, string? memberPath = null, string? memberWrapper = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        UrlPathOrAbsolute = urlPathOrAbsolute;
        MemberPath = memberPath;
        MemberWrapper = memberWrapper;
        Headers = headers;
    }
}
