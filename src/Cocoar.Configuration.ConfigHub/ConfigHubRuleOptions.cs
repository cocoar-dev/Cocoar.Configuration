using System.Text.Json.Serialization;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// Combined options for a <c>FromConfigHub</c> configuration rule.
/// </summary>
public sealed class ConfigHubRuleOptions
{
    /// <summary>
    /// Absolute HTTP or HTTPS ConfigHub delivery URL.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Bearer token issued for the ConfigHub delivery endpoint.
    /// </summary>
    [JsonIgnore]
    public string DeliveryToken { get; }

    /// <summary>
    /// Optional interval for conditional safety-net polling alongside SSE.
    /// </summary>
    public TimeSpan? FallbackPollInterval { get; }

    /// <summary>
    /// Optional maximum time without an SSE line before reconnecting.
    /// </summary>
    public TimeSpan? SseReadIdleTimeout { get; }

    /// <summary>
    /// Optional caller-owned handler used for ConfigHub HTTP requests.
    /// </summary>
    [JsonIgnore]
    public HttpMessageHandler? Handler { get; }

    /// <summary>
    /// Creates the combined options for a ConfigHub rule.
    /// </summary>
    /// <param name="url">Absolute HTTP or HTTPS ConfigHub delivery URL.</param>
    /// <param name="deliveryToken">Bearer token issued for the delivery endpoint.</param>
    /// <param name="fallbackPollInterval">Optional interval for conditional safety-net polling.</param>
    /// <param name="sseReadIdleTimeout">Optional maximum idle time before reconnecting the SSE stream.</param>
    /// <param name="handler">Optional caller-owned HTTP handler. The provider does not dispose it.</param>
    public ConfigHubRuleOptions(
        string url,
        string deliveryToken,
        TimeSpan? fallbackPollInterval = null,
        TimeSpan? sseReadIdleTimeout = null,
        HttpMessageHandler? handler = null)
    {
        Url = url;
        DeliveryToken = deliveryToken;
        FallbackPollInterval = fallbackPollInterval;
        SseReadIdleTimeout = sseReadIdleTimeout;
        Handler = handler;
    }

    internal ConfigHubProviderOptions ToProviderOptions() => new(
        FallbackPollInterval,
        SseReadIdleTimeout,
        Handler);

    internal ConfigHubProviderQueryOptions ToQueryOptions() => new(Url, DeliveryToken);
}
