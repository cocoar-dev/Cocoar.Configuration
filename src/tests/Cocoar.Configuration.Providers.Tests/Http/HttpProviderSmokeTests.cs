using System.Net;
using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.HttpPolling;
using Cocoar.Configuration.MicrosoftAdapter;
using Cocoar.Configuration.Providers.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.Http;

public class HttpProviderSmokeTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_ReadsJson_FromHandler()
    {
        var handler = new FakeHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        var provider =
            new HttpPollingProvider(new("https://example.com", TimeSpan.FromMilliseconds(50),
                handler));
        var result = await provider.FetchConfigurationBytesAsync(new("/api/config"));
        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        Assert.True(result.ToJsonElement().TryGetProperty("Value", out var v));
        Assert.Equal(1, v.GetInt32());
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task ConfigManager_Recompute_OnChange_Required()
    {
        // two responses: first value=1 then value=2
        var queue = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{ \"Value\": 2 }", Encoding.UTF8, "application/json") }
        });
        var handler = new QueueHandler(queue);

        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            // Provide base settings with Url via in-memory Microsoft IConfigurationSource (adapter)
            rules.For<MyHttpPollingSettings>().FromMicrosoftSource(cm => new(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Remote:Url"] = "/api/config" })
                        .Sources[0],
                    configurationPrefix: "Remote"
                )),
            rules.For<MyCfg>().FromHttpPolling(configManager => new(
                    urlPathOrAbsolute: configManager.GetRequiredConfig<MyHttpPollingSettings>().Url,
                    baseAddress: "https://example.com",
                    // Give CI plenty of time; we will actively wait for the change
                    pollInterval: TimeSpan.FromMilliseconds(50),
                    handler: handler
                ))
                .When(_ => true)
        ]));
        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<ConfigManager>();
        var first = manager.GetConfig<MyCfg>();
        Assert.Equal(1, first!.Value);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        MyCfg? second = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            await Task.Delay(40);
            second = manager.GetConfig<MyCfg>();
            if (second?.Value == 2)
            {
                break;
            }
        }

        Assert.Equal(2, second?.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_DoesNotEmit_OnSubscribe()
    {

        var handler = new FakeHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        // Use a large interval so even on slow CI a short wait won't reach first tick
        var provider =
            new HttpPollingProvider(new("https://example.com", TimeSpan.FromSeconds(2),
                handler));

        var emitted = false;
        using var sub = provider
            .ChangesAsBytes(new("/api/config"))
            .Subscribe(_ => emitted = true);

        // Wait well below the 2s poll interval to validate no immediate emission on subscribe
        await Task.Delay(500);
        Assert.False(emitted);
    }

    public class MyCfg
    {
        public int Value { get; set; }
    }

    public class MyHttpPollingSettings : MyCfg
    {
        public string Url { get; set; } = "/api/config";
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public FakeHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(Clone(_response));

        private static HttpResponseMessage Clone(HttpResponseMessage resp)
            => new(resp.StatusCode)
            {
                Content = resp.Content is null
                    ? null
                    : new StringContent(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult(), Encoding.UTF8,
                        resp.Content.Headers.ContentType?.MediaType ?? "application/json")
            };
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue;
        private HttpResponseMessage? _last;
        public QueueHandler(Queue<HttpResponseMessage> queue) => _queue = queue;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_queue.Count > 0)
            {
                _last = _queue.Dequeue();
                return Task.FromResult(_last);
            }

            // Return last known response to keep config steady
            var fallback = _last ?? new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{ }", Encoding.UTF8, "application/json") };
            return Task.FromResult(fallback);
        }
    }
}
