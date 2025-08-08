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
}
