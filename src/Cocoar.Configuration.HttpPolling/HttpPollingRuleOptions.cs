using System.Net.Http;

namespace Cocoar.Configuration.HttpPolling;

// Combined options for HTTP provider (instance + query) for fluent API
public sealed class HttpPollingRuleOptions
{
    // Instance-level
    public string? BaseAddress { get; }
    public TimeSpan? PollInterval { get; }
    public HttpMessageHandler? Handler { get; }

    // Query-level
    public string UrlPathOrAbsolute { get; }
    public string? SectionPath { get; }
    public string? WrapperPath { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingRuleOptions(
        string urlPathOrAbsolute,
        string? sectionPath = null,
        string? wrapperPath = null,
        string? baseAddress = null,
        TimeSpan? pollInterval = null,
        HttpMessageHandler? handler = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (string.IsNullOrWhiteSpace(urlPathOrAbsolute)) throw new ArgumentException("urlPathOrAbsolute is required", nameof(urlPathOrAbsolute));
        UrlPathOrAbsolute = urlPathOrAbsolute;
        SectionPath = sectionPath;
        WrapperPath = wrapperPath;
        BaseAddress = baseAddress;
        PollInterval = pollInterval;
        Handler = handler;
        Headers = headers;
    }

    public HttpPollingProviderOptions ToProviderOptions() => new(BaseAddress, PollInterval, Handler);
    public HttpPollingProviderQueryOptions ToQueryOptions() => new(UrlPathOrAbsolute, SectionPath, WrapperPath, Headers);
}
