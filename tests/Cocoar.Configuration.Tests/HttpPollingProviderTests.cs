using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Configuration.Providers.HttpPollingProvider;
using Cocoar.Configuration.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Tests;

public class HttpPollingProviderTests
{
    [Fact]
    public async Task GetValueAsync_ReadsJson_FromHandler()
    {
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        var provider = new HttpPollingProvider(new HttpPollingProviderOptions("https://example.com", TimeSpan.FromMilliseconds(50), handler));
        var result = await provider.GetValueAsync(new HttpPollingProviderQueryOptions("/api/config"));
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
            HttpPollingProvider.CreateRule<MyCfg, MyCfg>(
                optionsFactory: _ => new HttpPollingProviderOptions("https://example.com", TimeSpan.FromMilliseconds(10), handler),
                queryFactory: _ => new HttpPollingProviderQueryOptions("/api/config"),
                useWhen: () => true
            )
        ]);
        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<ConfigManager>();
    var first = manager.GetConfig<MyCfg>();
        // wait until next poll
        await Task.Delay(30);
    var second = manager.GetConfig<MyCfg>();
    Assert.Equal(2, second!.Value);
    }

    public class MyCfg
    {
        public int Value { get; set; }
    }

    [Fact]
    public async Task Changes_DoesNotEmit_OnSubscribe()
    {
        // arrange: handler returns a valid body, but we only test the Changes() initial behavior
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
        });
        var provider = new HttpPollingProvider(new HttpPollingProviderOptions("https://example.com", TimeSpan.FromMilliseconds(200), handler));

        var emitted = false;
        using var sub = provider
            .Changes(new HttpPollingProviderQueryOptions("/api/config"))
            .Subscribe(_ => emitted = true);

        // assert: no initial emission before first interval elapses
        await Task.Delay(100);
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
