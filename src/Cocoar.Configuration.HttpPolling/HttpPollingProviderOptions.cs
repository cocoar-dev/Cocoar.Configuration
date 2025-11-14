using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProviderOptions : IProviderConfiguration
{
    public string? BaseAddress { get; }
    public TimeSpan PollInterval { get; }
    /// <summary>
    /// Number of consecutive poll failures before we emit an error signal to trigger a recompute.
    /// </summary>
    public int ErrorConsecutiveFailureThreshold { get; }
    public HttpMessageHandler? Handler { get; }

    public HttpPollingProviderOptions(string? baseAddress = null, TimeSpan? pollInterval = null,
        int errorConsecutiveFailureThreshold = 3,
        HttpMessageHandler? handler = null)
    {
        BaseAddress = baseAddress;
        PollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
        ErrorConsecutiveFailureThreshold = errorConsecutiveFailureThreshold <= 0 ? 3 : errorConsecutiveFailureThreshold;
        Handler = handler;
    }

    // Back-compat convenience overload to preserve existing call sites that pass handler as third argument
    public HttpPollingProviderOptions(string? baseAddress, TimeSpan? pollInterval, HttpMessageHandler? handler)
        : this(baseAddress, pollInterval, 3, handler)
    {
    }
}
