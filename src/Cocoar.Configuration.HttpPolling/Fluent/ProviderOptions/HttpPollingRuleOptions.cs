using System.Net.Http;

namespace Cocoar.Configuration.HttpPolling.Fluent.ProviderOptions;

// Combined options for HTTP provider (instance + query) for fluent API
public sealed class HttpPollingRuleOptions
{
    // Instance-level
    public string? BaseAddress { get; }
    public TimeSpan? PollInterval { get; }
    public HttpMessageHandler? Handler { get; }

    // Query-level
    public string UrlPathOrAbsolute { get; }
    public string? MemberPath { get; }
    public string? MemberWrapper { get; }
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpPollingRuleOptions(
        string urlPathOrAbsolute,
        string? memberPath = null,
        string? memberWrapper = null,
        string? baseAddress = null,
        TimeSpan? pollInterval = null,
        HttpMessageHandler? handler = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (string.IsNullOrWhiteSpace(urlPathOrAbsolute)) throw new ArgumentException("urlPathOrAbsolute is required", nameof(urlPathOrAbsolute));
        UrlPathOrAbsolute = urlPathOrAbsolute;
        MemberPath = memberPath;
        MemberWrapper = memberWrapper;
        BaseAddress = baseAddress;
        PollInterval = pollInterval;
        Handler = handler;
        Headers = headers;
    }

    // Converters are now created inline by HttpRuleBuilder to avoid cross-namespace coupling.
}
