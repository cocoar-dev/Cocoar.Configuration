using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProviderOptions : IProviderConfiguration
{
    public string? BaseAddress { get; }
    public TimeSpan PollInterval { get; }
    public HttpMessageHandler? Handler { get; }

    public HttpPollingProviderOptions(string? baseAddress = null, TimeSpan? pollInterval = null, HttpMessageHandler? handler = null)
    {
        BaseAddress = baseAddress;
        PollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
        Handler = handler;
    }
}
