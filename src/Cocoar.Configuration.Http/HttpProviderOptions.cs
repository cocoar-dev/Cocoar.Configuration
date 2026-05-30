using System.Text.Json;
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

    /// <summary>
    /// Optional factory for an externally-owned <see cref="HttpClient"/> — used by the service-backed
    /// (Layer-2, ADR-006) <c>FromHttp((sp,a)=&gt;…)</c> overload to source a client from
    /// <c>IHttpClientFactory</c>. The outer <c>(sp, accessor)</c> is captured at build time, but this factory is
    /// invoked per fetch and per SSE (re)connection, so the factory's handler rotation applies. When set, the
    /// provider does NOT dispose the client (the factory owns the pooled handler). Takes precedence over <see cref="Handler"/>.
    /// Not serialized for provider key generation.
    /// </summary>
    [JsonIgnore]
    public Func<HttpClient>? ClientFactory { get; }

    public HttpProviderOptions(
        TimeSpan? pollInterval = null,
        bool serverSentEvents = false,
        TimeSpan? fallbackPollInterval = null,
        int errorConsecutiveFailureThreshold = 3,
        HttpMessageHandler? handler = null,
        Func<HttpClient>? clientFactory = null)
    {
        PollInterval = pollInterval;
        ServerSentEvents = serverSentEvents;
        FallbackPollInterval = fallbackPollInterval;
        ErrorConsecutiveFailureThreshold = errorConsecutiveFailureThreshold <= 0 ? 3 : errorConsecutiveFailureThreshold;
        Handler = handler;
        ClientFactory = clientFactory;
    }

    private static readonly JsonSerializerOptions ProviderKeyOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    /// <summary>
    /// A service-backed client (from <c>IHttpClientFactory</c>) is externally owned and per-rule — two rules with
    /// distinct clients must never collapse onto one shared <see cref="HttpProvider"/>. Since <see cref="ClientFactory"/>
    /// is <c>[JsonIgnore]</c>d and so invisible to the default serialized key, return <c>null</c> (never share) in that
    /// case — mirroring <c>LocalStorageProviderOptions</c>. The Layer-1 (no-factory) key is unchanged.
    /// </summary>
    public string? GenerateProviderKey()
        => ClientFactory is not null
            ? null
            : JsonSerializer.Serialize(this, typeof(HttpProviderOptions), ProviderKeyOptions);
}
