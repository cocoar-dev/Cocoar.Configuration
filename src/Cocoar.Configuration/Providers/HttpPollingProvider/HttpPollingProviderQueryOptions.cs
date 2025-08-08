namespace Cocoar.Configuration.Providers.HttpPollingProvider;

public sealed class HttpPollingProviderQueryOptions : ISourceProviderQueryOptions
{
    public string? MemberPath { get; }
    public string? MemberWrapper { get; }

    // Relative path or full URL if BaseAddress is null
    public string UrlPathOrAbsolute { get; }

    public HttpPollingProviderQueryOptions(string urlPathOrAbsolute, string? memberPath = null, string? memberWrapper = null)
    {
        UrlPathOrAbsolute = urlPathOrAbsolute;
        MemberPath = memberPath;
        MemberWrapper = memberWrapper;
    }
}
