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

    /// <summary>Dimensions this client reports (key → value), e.g. <c>server</c> → the machine name.</summary>
    public IReadOnlyDictionary<string, string> Dimensions { get; }

    /// <summary>How warnings about the reported dimensions are treated (default: warn).</summary>
    public ConfigHubWarningMode WarningMode { get; }

    /// <summary>Optional callback invoked when the warnings returned by ConfigHub change.</summary>
    [JsonIgnore]
    public Action<IReadOnlyList<string>>? OnWarnings { get; }

    /// <summary>
    /// Creates the combined options for a ConfigHub rule.
    /// </summary>
    /// <param name="url">Absolute HTTP or HTTPS ConfigHub delivery URL.</param>
    /// <param name="deliveryToken">Bearer token issued for the delivery endpoint.</param>
    /// <param name="fallbackPollInterval">Optional interval for conditional safety-net polling.</param>
    /// <param name="sseReadIdleTimeout">Optional maximum idle time before reconnecting the SSE stream.</param>
    /// <param name="handler">Optional caller-owned HTTP handler. The provider does not dispose it.</param>
    /// <param name="dimensions">Optional dimensions this client reports (key → value).</param>
    /// <param name="warningMode">How warnings about the reported dimensions are treated.</param>
    /// <param name="onWarnings">Optional callback invoked when the returned warnings change.</param>
    public ConfigHubRuleOptions(
        string url,
        string deliveryToken,
        TimeSpan? fallbackPollInterval = null,
        TimeSpan? sseReadIdleTimeout = null,
        HttpMessageHandler? handler = null,
        IReadOnlyDictionary<string, string>? dimensions = null,
        ConfigHubWarningMode warningMode = ConfigHubWarningMode.Warn,
        Action<IReadOnlyList<string>>? onWarnings = null)
    {
        Url = url;
        DeliveryToken = deliveryToken;
        FallbackPollInterval = fallbackPollInterval;
        SseReadIdleTimeout = sseReadIdleTimeout;
        Handler = handler;
        Dimensions = dimensions ?? new Dictionary<string, string>();
        WarningMode = warningMode;
        OnWarnings = onWarnings;
    }

    /// <summary>
    /// Returns a copy that also reports <paramref name="key"/> = <paramref name="value"/>, e.g.
    /// <c>.WithDimension("server", Environment.MachineName)</c>. Values computed from other
    /// configuration go through the config-aware <c>FromConfigHub(accessor => …)</c> overload.
    /// </summary>
    public ConfigHubRuleOptions WithDimension(string key, string? value)
    {
        var dimensions = new Dictionary<string, string>(Dimensions, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            dimensions.Remove(key);
        else
            dimensions[key] = value;
        return Copy(dimensions, WarningMode, OnWarnings);
    }

    /// <summary>Returns a copy with the given handling of warnings about the reported dimensions.</summary>
    public ConfigHubRuleOptions WithWarnings(ConfigHubWarningMode mode, Action<IReadOnlyList<string>>? onWarnings = null)
        => Copy(Dimensions, mode, onWarnings ?? OnWarnings);

    private ConfigHubRuleOptions Copy(
        IReadOnlyDictionary<string, string> dimensions, ConfigHubWarningMode mode, Action<IReadOnlyList<string>>? onWarnings)
        => new(Url, DeliveryToken, FallbackPollInterval, SseReadIdleTimeout, Handler, dimensions, mode, onWarnings);

    internal ConfigHubProviderOptions ToProviderOptions() => new(
        FallbackPollInterval,
        SseReadIdleTimeout,
        Handler);

    internal ConfigHubProviderQueryOptions ToQueryOptions() => new(Url, DeliveryToken, Dimensions, WarningMode, OnWarnings);
}
