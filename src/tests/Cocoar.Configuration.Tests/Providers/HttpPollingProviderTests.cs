using System.Net;
using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.HttpPolling;
using Cocoar.Configuration.MicrosoftAdapter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.Tests;

public class HttpPollingProviderTests
{
    [Fact]
    public async Task FetchConfigurationAsync_ReadsJson_FromHandler()
    {
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        var provider = new HttpPollingProvider(new HttpPollingProviderOptions("https://example.com", TimeSpan.FromMilliseconds(50), handler));
        var result = await provider.FetchConfigurationAsync(new HttpPollingProviderQueryOptions("/api/config"));
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    Assert.True(result.TryGetProperty("Value", out var v));
        Assert.Equal(1, v.GetInt32());
    }

    [Fact]
    public async Task ConfigManager_Recompute_OnChange_Required()
    {
        // two responses: first value=1 then value=2
        var queue = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"Value\": 2 }", Encoding.UTF8, "application/json") }
        });
        var handler = new QueueHandler(queue);

    var services = new ServiceCollection();
        services.AddCocoarConfiguration([
            // Provide base settings with Url via in-memory Microsoft IConfigurationSource (adapter)
            Rule.From
                .MicrosoftSource(cm => new MicrosoftConfigurationSourceRuleOptions(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string,string?> { ["Remote:Url"] = "/api/config" })
                        .Sources[0],
                    configurationPrefix: "Remote"
                ))
                .For<MyHttpPollingSettings>()
                .Optional(),
            Rule.From
                .HttpPolling(configManager => new HttpPollingRuleOptions(
                    urlPathOrAbsolute: configManager.GetRequiredConfig<MyHttpPollingSettings>().Url,
                    baseAddress: "https://example.com",
            // Give CI plenty of time; we will actively wait for the change
            pollInterval: TimeSpan.FromMilliseconds(50),
                    handler: handler
                ))
                .When(() => true)
                .For<MyCfg>()
        ]);
        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<ConfigManager>();
        var first = manager.GetConfig<MyCfg>();
        Assert.Equal(1, first!.Value);
        // Actively wait (up to 3s) for value to become 2 to avoid timing flakiness
        var sw = System.Diagnostics.Stopwatch.StartNew();
        MyCfg? second = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            await Task.Delay(40);
            second = manager.GetConfig<MyCfg>();
            if (second?.Value == 2) break;
        }
        Assert.Equal(2, second?.Value);
    }

    public class MyCfg
    {
        public int Value { get; set; }
    }

    public class MyHttpPollingSettings : MyCfg
    {
        public string Url { get; set; } = "/api/config";
    }

    [Fact]
    public async Task Changes_DoesNotEmit_OnSubscribe()
    {
        // arrange: handler returns a valid body, but we only test the Changes() initial behavior
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
    // Use a large interval so even on slow CI a short wait won't reach first tick
    var provider = new HttpPollingProvider(new HttpPollingProviderOptions("https://example.com", TimeSpan.FromSeconds(2), handler));

        var emitted = false;
        using var sub = provider
            .Changes(new HttpPollingProviderQueryOptions("/api/config"))
            .Subscribe(_ => emitted = true);

        // assert: no initial emission before first interval elapses
    await Task.Delay(150); // still far below 2s interval
        Assert.False(emitted);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public FakeHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Clone(_response));

        private static HttpResponseMessage Clone(HttpResponseMessage resp)
            => new(resp.StatusCode) { Content = resp.Content is null ? null : new StringContent(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult(), Encoding.UTF8, resp.Content.Headers.ContentType?.MediaType ?? "application/json") };
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue;
        private HttpResponseMessage? _last;
        public QueueHandler(Queue<HttpResponseMessage> queue) => _queue = queue;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_queue.Count > 0)
            {
                _last = _queue.Dequeue();
                return Task.FromResult(_last);
            }
            // Return last known response to keep config steady
            var fallback = _last ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ }", Encoding.UTF8, "application/json") };
            return Task.FromResult(fallback);
        }
    }
}
