using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// Connection-level options for the ConfigHub configuration provider.
/// </summary>
public sealed class ConfigHubProviderOptions : IProviderConfiguration
{
    private static readonly JsonSerializerOptions ProviderKeyOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    /// <summary>
    /// Optional interval for a conditional HTTP poll that runs alongside SSE as a safety net.
    /// </summary>
    public TimeSpan? FallbackPollInterval { get; }

    /// <summary>
    /// Optional maximum time without an SSE line before the connection is re-established.
    /// ConfigHub sends keep-alive comments, so this may safely be longer than its keep-alive interval.
    /// </summary>
    public TimeSpan? SseReadIdleTimeout { get; }

    /// <summary>
    /// Optional caller-owned handler used by the provider's HTTP client.
    /// </summary>
    [JsonIgnore]
    public HttpMessageHandler? Handler { get; }

    /// <summary>
    /// Creates connection-level options for ConfigHub delivery.
    /// </summary>
    /// <param name="fallbackPollInterval">Optional interval for conditional safety-net polling.</param>
    /// <param name="sseReadIdleTimeout">Optional maximum idle time before reconnecting the SSE stream.</param>
    /// <param name="handler">Optional caller-owned HTTP handler. The provider does not dispose it.</param>
    public ConfigHubProviderOptions(
        TimeSpan? fallbackPollInterval = null,
        TimeSpan? sseReadIdleTimeout = null,
        HttpMessageHandler? handler = null)
    {
        ValidatePositive(fallbackPollInterval, nameof(fallbackPollInterval));
        ValidatePositive(sseReadIdleTimeout, nameof(sseReadIdleTimeout));

        FallbackPollInterval = fallbackPollInterval;
        SseReadIdleTimeout = sseReadIdleTimeout;
        Handler = handler;
    }

    /// <inheritdoc />
    public string? GenerateProviderKey()
        => Handler is null
            ? JsonSerializer.Serialize(this, ProviderKeyOptions)
            : null;

    private static void ValidatePositive(TimeSpan? value, string parameterName)
    {
        if (value is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The interval must be greater than zero.");
        }
    }
}
