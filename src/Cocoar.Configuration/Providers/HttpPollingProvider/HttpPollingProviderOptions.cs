using System.Net.Http;

namespace Cocoar.Configuration.Providers.HttpPollingProvider;

public sealed class HttpPollingProviderOptions : ISourceProviderInstanceOptions
{
    public string? BaseAddress { get; }
    public TimeSpan PollInterval { get; }

    // For testing/mocking; if null, default HttpClientHandler is used
    public HttpMessageHandler? Handler { get; }

    public HttpPollingProviderOptions(string? baseAddress = null, TimeSpan? pollInterval = null, HttpMessageHandler? handler = null)
    {
        BaseAddress = baseAddress;
        PollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
        Handler = handler;
    }

    // Reuse client across rules; key by handler identity and polling interval only.
    // BaseAddress is intentionally excluded so queries can use absolute URLs or combine with per-query paths.
    public string CalculateKey()
    {
        var handlerKey = Handler is null ? "default" : Handler.GetHashCode().ToString();
        return $"handler:{handlerKey}|poll:{PollInterval.TotalMilliseconds}";
    }
}
