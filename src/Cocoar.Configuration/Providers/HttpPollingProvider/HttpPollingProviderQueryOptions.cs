using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.HttpPollingProvider;

public sealed class HttpPollingProviderQueryOptions : ISourceProviderQueryOptions
{
    // Relative path or full URL if BaseAddress is null
    public string UrlPathOrAbsolute { get; }
    public string? SectionPath { get; }
    public string? WrapperPath { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingProviderQueryOptions(string urlPathOrAbsolute, string? sectionPath = null, string? wrapperPath = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        UrlPathOrAbsolute = urlPathOrAbsolute;
        SectionPath = sectionPath;
        WrapperPath = wrapperPath;
        Headers = headers;
    }
}
