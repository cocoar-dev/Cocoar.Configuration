using System.Net;
using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Http;
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
    [Trait("Provider", "HttpProvider")]
    public async Task FetchConfigurationAsync_ReadsJson_FromHandler()
    {
        var handler = new FakeHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        var provider =
            new HttpProvider(new(pollInterval: TimeSpan.FromMilliseconds(50),
                handler: handler));
        var result = await provider.FetchConfigurationBytesAsync(new("https://example.com/api/config"));
        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        Assert.True(result.ToJsonElement().TryGetProperty("Value", out var v));
        Assert.Equal(1, v.GetInt32());
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Provider", "HttpProvider")]
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
            rules.For<MyHttpSettings>().FromIConfiguration(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Url"] = "https://example.com/api/config" })
                        .Build()),
            rules.For<MyCfg>().FromHttp(configManager => new(
                    url: configManager.GetConfig<MyHttpSettings>()!.Url,
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
    [Trait("Provider", "HttpProvider")]
    public async Task Changes_DoesNotEmit_OnSubscribe()
    {

        var handler = new FakeHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        // Use a large interval so even on slow CI a short wait won't reach first tick
        var provider =
            new HttpProvider(new(pollInterval: TimeSpan.FromSeconds(2),
                handler: handler));

        var emitted = false;
        using var sub = provider
            .ChangesAsBytes(new("https://example.com/api/config"))
            .Subscribe(_ => emitted = true);

        // Wait well below the 2s poll interval to validate no immediate emission on subscribe
        await Task.Delay(500);
        Assert.False(emitted);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpProvider")]
    public async Task OneTimeFetch_Changes_ReturnsNever()
    {
        var handler = new FakeHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        // No poll interval, no SSE — one-time fetch mode
        var provider = new HttpProvider(new(handler: handler));

        var emitted = false;
        using var sub = provider
            .ChangesAsBytes(new("https://example.com/api/config"))
            .Subscribe(_ => emitted = true);

        // Fetch still works for initial load
        var result = await provider.FetchConfigurationBytesAsync(new("https://example.com/api/config"));
        Assert.Equal(1, result.ToJsonElement().GetProperty("Value").GetInt32());

        // But changes never fires
        await Task.Delay(300);
        Assert.False(emitted);
    }

    public class MyCfg
    {
        public int Value { get; set; }
    }

    public class MyHttpSettings : MyCfg
    {
        public string Url { get; set; } = "https://example.com/api/config";
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
