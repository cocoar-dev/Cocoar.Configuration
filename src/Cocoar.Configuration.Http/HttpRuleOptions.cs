namespace Cocoar.Configuration.Http;

/// <summary>
/// Combined options for HTTP configuration rules, covering both provider instance
/// and query-level settings. Used by the <c>FromHttp</c> fluent API.
/// </summary>
public sealed class HttpRuleOptions
{
    /// <summary>
    /// The URL path (relative to base address) or absolute URL to fetch configuration from.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// When set, the provider polls at this interval. When null (and SSE is disabled),
    /// configuration is fetched once and never updated.
    /// </summary>
    public TimeSpan? PollInterval { get; }

    /// <summary>
    /// When true, the provider opens a Server-Sent Events connection for live updates.
    /// </summary>
    public bool ServerSentEvents { get; }

    /// <summary>
    /// When SSE is enabled, falls back to polling at this interval if the SSE connection
    /// cannot be established or is lost for an extended period. Ignored when SSE is disabled.
    /// </summary>
    public TimeSpan? FallbackPollInterval { get; }

    /// <summary>
    /// Optional HTTP headers sent with every request (fetch, poll, and SSE).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; }

    /// <summary>
    /// Optional custom <see cref="HttpMessageHandler"/> for the underlying <see cref="HttpClient"/>.
    /// The handler is NOT disposed by the provider.
    /// </summary>
    public HttpMessageHandler? Handler { get; }

    /// <summary>
    /// Number of consecutive failures before emitting an empty sentinel to trigger health degradation.
    /// </summary>
    public int ErrorConsecutiveFailureThreshold { get; }

    /// <summary>
    /// When set, an SSE connection that receives no data within this window is treated as dead and
    /// reconnected (with backoff). Default <c>null</c> = no read-idle timeout. Only meaningful when
    /// <see cref="ServerSentEvents"/> is true. Set this only if your SSE server sends periodic data or
    /// heartbeat comments; otherwise a legitimately idle config stream would reconnect needlessly.
    /// </summary>
    public TimeSpan? SseReadIdleTimeout { get; }

    public HttpRuleOptions(
        string url,
        TimeSpan? pollInterval = null,
        bool serverSentEvents = false,
        TimeSpan? fallbackPollInterval = null,
        IReadOnlyDictionary<string, string>? headers = null,
        HttpMessageHandler? handler = null,
        int errorConsecutiveFailureThreshold = 3,
        TimeSpan? sseReadIdleTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("url is required", nameof(url));
        }

        Url = url;
        PollInterval = pollInterval;
        ServerSentEvents = serverSentEvents;
        FallbackPollInterval = fallbackPollInterval;
        Headers = headers;
        Handler = handler;
        ErrorConsecutiveFailureThreshold = errorConsecutiveFailureThreshold <= 0 ? 3 : errorConsecutiveFailureThreshold;
        SseReadIdleTimeout = sseReadIdleTimeout;
    }

    internal HttpProviderOptions ToProviderOptions() => new(
        PollInterval,
        ServerSentEvents,
        FallbackPollInterval,
        ErrorConsecutiveFailureThreshold,
        Handler,
        sseReadIdleTimeout: SseReadIdleTimeout);

    internal HttpProviderQueryOptions ToQueryOptions() => new(Url, Headers);
}
