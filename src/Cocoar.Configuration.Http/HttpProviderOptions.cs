using System.Text.Json.Serialization;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Http;

/// <summary>
/// Instance-level configuration for <see cref="HttpProvider"/>.
/// Determines the change-tracking mode (polling, SSE, or one-time fetch).
/// </summary>
public sealed class HttpProviderOptions : IProviderConfiguration
{
    /// <summary>
    /// Polling interval. When null (and SSE is disabled), the provider does a one-time fetch.
    /// </summary>
    public TimeSpan? PollInterval { get; }

    /// <summary>
    /// When true, the provider uses Server-Sent Events for live updates.
    /// </summary>
    public bool ServerSentEvents { get; }

    /// <summary>
    /// Fallback polling interval when SSE connection fails. Only used when <see cref="ServerSentEvents"/> is true.
    /// </summary>
    public TimeSpan? FallbackPollInterval { get; }

    /// <summary>
    /// Number of consecutive failures before emitting an empty sentinel to trigger health degradation.
    /// </summary>
    public int ErrorConsecutiveFailureThreshold { get; }

    /// <summary>
    /// Optional custom handler for the underlying HttpClient. Not serialized for provider key generation.
    /// </summary>
    [JsonIgnore]
    public HttpMessageHandler? Handler { get; }

    public HttpProviderOptions(
        TimeSpan? pollInterval = null,
        bool serverSentEvents = false,
        TimeSpan? fallbackPollInterval = null,
        int errorConsecutiveFailureThreshold = 3,
        HttpMessageHandler? handler = null)
    {
        PollInterval = pollInterval;
        ServerSentEvents = serverSentEvents;
        FallbackPollInterval = fallbackPollInterval;
        ErrorConsecutiveFailureThreshold = errorConsecutiveFailureThreshold <= 0 ? 3 : errorConsecutiveFailureThreshold;
        Handler = handler;
    }
}
