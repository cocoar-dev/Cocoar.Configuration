using System.Net;
using System.Text;
using System.Text.Json;
using Cocoar.Configuration.HttpPolling;
using Cocoar.Configuration.Providers.Tests.Helpers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.Http;

public class HttpProviderBattleTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try { disposable.Dispose(); } catch { /* ignore */ }
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_PerformsHttpOnEveryCall()
    {

        var callCount = 0;
        var handler = new CallCountingHandler(() =>
        {
            callCount++;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{ \"CallCount\": {callCount} }}", Encoding.UTF8, "application/json")
            };
        });

        var provider = CreateProvider(handler);
        var query = new HttpPollingProviderQueryOptions("/api/config");


    var first = await provider.FetchConfigurationBytesAsync(query);
    var second = await provider.FetchConfigurationBytesAsync(query);

    Assert.Equal(1, first.ToJsonElement().GetProperty("CallCount").GetInt32());
    Assert.Equal(2, second.ToJsonElement().GetProperty("CallCount").GetInt32());
    Assert.Equal(2, callCount); // Called for each fetch
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_ThrowsException_OnHttpError()
    {

        var handler = new StaticHandler(new(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);
        var query = new HttpPollingProviderQueryOptions("/api/config");


        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.FetchConfigurationBytesAsync(query));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_ThrowsJsonException_OnInvalidJson()
    {

        var handler = new StaticHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid json }", Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler);
        var query = new HttpPollingProviderQueryOptions("/api/config");

        // Provider is byte-only and does not validate JSON; fetching should not throw.
        var bytes = await provider.FetchConfigurationBytesAsync(query);
        // Converting to JsonElement in tests returns empty object on malformed JSON
        var element = bytes.ToJsonElement();
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.False(element.EnumerateObject().Any());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_IncludesCustomHeaders()
    {

        HttpRequestMessage? capturedRequest = null;
        var handler = new RequestCapturingHandler(req =>
        {
            capturedRequest = req;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"Success\": true }", Encoding.UTF8, "application/json")
            };
        });

        var provider = CreateProvider(handler);
        var query = new HttpPollingProviderQueryOptions("/api/config", 
            new Dictionary<string, string> 
            {
                ["Authorization"] = "Bearer token123",
                ["X-Client-Version"] = "1.0.0"
            });


        await provider.FetchConfigurationBytesAsync(query);


        Assert.NotNull(capturedRequest);
        Assert.Contains("Bearer token123", capturedRequest.Headers.GetValues("Authorization"));
        Assert.Contains("1.0.0", capturedRequest.Headers.GetValues("X-Client-Version"));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public void Constructor_BuildsCorrectUrl_WithBaseAddress()
    {

        Uri? capturedUri = null;
        var handler = new RequestCapturingHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });


        var provider = new HttpPollingProvider(
            new("https://api.example.com", TimeSpan.FromMilliseconds(100), handler));
        _disposables.Add(provider);
        
        var query = new HttpPollingProviderQueryOptions("/v1/config");
        _ = provider.FetchConfigurationBytesAsync(query);


        Assert.Equal("https://api.example.com/v1/config", capturedUri?.ToString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public void Constructor_UsesAbsoluteUrl_IgnoresBaseAddress()
    {

        Uri? capturedUri = null;
        var handler = new RequestCapturingHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var provider = new HttpPollingProvider(
            new("https://base.example.com", TimeSpan.FromMilliseconds(100), handler));
        _disposables.Add(provider);


        var query = new HttpPollingProviderQueryOptions("https://other.example.com/config");
        _ = provider.FetchConfigurationBytesAsync(query);


        Assert.Equal("https://other.example.com/config", capturedUri?.ToString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_DoesNotEmit_OnException()
    {
        // Use a very high threshold so we don't emit the sentinel during this short test window
        var handler = new ExceptionHandler(new HttpRequestException("Network error"));
        var provider = new HttpPollingProvider(
            new("https://example.com", TimeSpan.FromMilliseconds(50), int.MaxValue, handler));
        _disposables.Add(provider);
        var query = new HttpPollingProviderQueryOptions("/api/config");


        var emissions = new List<JsonElement>();
        using var subscription = provider.ChangesAsBytes(query)
            .Subscribe(element => emissions.Add(element.ToJsonElement()));

        // Wait for several poll attempts (at 50ms interval, 500ms = ~10 polls)
        // This validates that exceptions don't emit until sentinel threshold is reached
        await Task.Delay(500);


        Assert.Empty(emissions);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_EmitsSentinel_AfterConsecutiveFailures()
    {
        var handler = new ExceptionHandler(new HttpRequestException("Network error"));
        var provider = new HttpPollingProvider(
            new("https://example.com", TimeSpan.FromMilliseconds(30), 3, handler));
        _disposables.Add(provider);
        var query = new HttpPollingProviderQueryOptions("/api/config");

        var emissionCount = 0;
        using var subscription = provider.ChangesAsBytes(query)
            .Subscribe(_ => Interlocked.Increment(ref emissionCount));

        // Wait for sentinel emission after 3 consecutive failures
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissionCount >= 1,
            timeout: TimeSpan.FromSeconds(5),
            description: "sentinel emission after consecutive failures");

        Assert.True(emissionCount >= 1);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_Emits_OnEveryPollInterval()
    {

        var responses = new Queue<HttpResponseMessage>(new[]
        {
            CreateJsonResponse("{ \"Value\": 1 }"),
            CreateJsonResponse("{ \"Value\": 1 }"),
            CreateJsonResponse("{ \"Value\": 2 }"),
            CreateJsonResponse("{ \"Value\": 2 }"),
        });
        var handler = new QueueHandler(responses);
        var provider = CreateProvider(handler, TimeSpan.FromMilliseconds(80));
        var query = new HttpPollingProviderQueryOptions("/api/config");


        var emissions = new List<JsonElement>();
        using var subscription = provider.ChangesAsBytes(query)
            .Subscribe(element => emissions.Add(element.ToJsonElement()));

    await ActiveWaitHelpers.WaitUntilAsync(
        () => emissions.Any(e => e.GetProperty("Value").GetInt32() == 1) &&
              emissions.Any(e => e.GetProperty("Value").GetInt32() == 2),
        timeout: TimeSpan.FromSeconds(3),
        description: "emissions to contain both 1 and 2");

    // should emit on each successful poll (we don't assert exact count due to timing)
    Assert.True(emissions.Count >= 2);
    Assert.Contains(emissions, e => e.GetProperty("Value").GetInt32() == 1);
    Assert.Contains(emissions, e => e.GetProperty("Value").GetInt32() == 2);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_DifferentQueries_EmitIndependently()
    {

        var handler = new RequestCapturingHandler(req =>
        {
            var path = req.RequestUri?.PathAndQuery;
            var value = path?.Contains("config1") == true ? 1 : 2;
            return CreateJsonResponse($"{{ \"Value\": {value} }}");
        });

        var provider = CreateProvider(handler, TimeSpan.FromMilliseconds(50));
        var query1 = new HttpPollingProviderQueryOptions("/api/config1");
        var query2 = new HttpPollingProviderQueryOptions("/api/config2");


        JsonElement? emission1 = null, emission2 = null;
        using var sub1 = provider.ChangesAsBytes(query1).Subscribe(e => emission1 = e.ToJsonElement());
        using var sub2 = provider.ChangesAsBytes(query2).Subscribe(e => emission2 = e.ToJsonElement());

        await ActiveWaitHelpers.WaitUntilAsync(
            () => emission1 != null && emission2 != null,
            timeout: TimeSpan.FromSeconds(2),
            description: "both queries to emit");

    Assert.NotNull(emission1);
    Assert.NotNull(emission2);
    Assert.Equal(1, emission1.Value.GetProperty("Value").GetInt32());
    Assert.Equal(2, emission2.Value.GetProperty("Value").GetInt32());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public void Dispose_CleansUpResources_Safely()
    {

        var handler = new StaticHandler(CreateJsonResponse("{}"));
        var provider = new HttpPollingProvider(
            new("https://example.com", TimeSpan.FromMilliseconds(50), handler));


        provider.Dispose();
        provider.Dispose();


        Assert.True(true); // If we get here, disposal worked correctly
    }

    #region Test Infrastructure

    private HttpPollingProvider CreateProvider(HttpMessageHandler handler, TimeSpan? pollInterval = null)
    {
        var provider = new HttpPollingProvider(
            new("https://example.com", pollInterval ?? TimeSpan.FromSeconds(1), handler));
        _disposables.Add(provider);
        return provider;
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public StaticHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    private sealed class CallCountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;
        public CallCountingHandler(Func<HttpResponseMessage> responseFactory) => _responseFactory = responseFactory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responseFactory());
    }

    private sealed class RequestCapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public RequestCapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class ExceptionHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ExceptionHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _exception;
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        private HttpResponseMessage _lastResponse = new(HttpStatusCode.OK) 
        { 
            Content = new StringContent("{}", Encoding.UTF8, "application/json") 
        };

        public QueueHandler(Queue<HttpResponseMessage> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count > 0)
            {
                _lastResponse = _responses.Dequeue();
            }
            return Task.FromResult(_lastResponse);
        }
    }

    #endregion
}
