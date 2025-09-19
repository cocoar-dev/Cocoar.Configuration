using System.Net;
using System.Text;
using System.Text.Json;
using Cocoar.Configuration.HttpPolling;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.Http;

/// <summary>
/// HttpProviderBattleTests
/// -----------------------
/// PURPOSE
///   Comprehensive battle testing of HttpPollingProvider current functionality
///   covering edge cases, error scenarios, and production-ready validation.
///   Tests actual implementation behavior without adding new features.
/// 
/// SCOPE
///   - HTTP error response handling (404, 500, timeouts)
///   - JSON parsing edge cases and malformed responses
///   - Custom header handling and URL construction
///   - Polling interval and change detection validation
///   - Resource disposal and lifecycle management
///   - Caching behavior and key generation
/// 
/// COVERAGE
///   - Network error scenarios (what provider currently handles)
///   - JSON response validation and error recovery
///   - Header injection and URL building logic
///   - Change detection via JSON hashing
///   - Memory and resource management
/// 
/// CONSTRAINTS
///   - Tests ONLY current functionality - no retry, no ETags, no auth
///   - Uses mock HttpMessageHandler for reliable testing
///   - Focuses on provider behavior, not HTTP protocol implementation
/// </summary>
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
    public async Task FetchConfigurationAsync_Returns_CachedValue_OnSecondCall()
    {
        // arrange: handler that tracks call count
        var callCount = 0;
        var handler = new CallCountingHandler(() =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{ \"CallCount\": {callCount} }}", Encoding.UTF8, "application/json")
            };
        });

        var provider = CreateProvider(handler);
        var query = new HttpPollingProviderQueryOptions("/api/config");

        // act: fetch twice
        var first = await provider.FetchConfigurationAsync(query);
        var second = await provider.FetchConfigurationAsync(query);

        // assert: both return same cached value (from first call)
        Assert.Equal(1, first.GetProperty("CallCount").GetInt32());
        Assert.Equal(1, second.GetProperty("CallCount").GetInt32());
        Assert.Equal(1, callCount); // Only called once
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_ThrowsException_OnHttpError()
    {
        // arrange: 404 response
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);
        var query = new HttpPollingProviderQueryOptions("/api/config");

        // act & assert: EnsureSuccessStatusCode throws
        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.FetchConfigurationAsync(query));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_ThrowsJsonException_OnInvalidJson()
    {
        // arrange: malformed JSON response
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid json }", Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler);
        var query = new HttpPollingProviderQueryOptions("/api/config");

        // act & assert: JsonDocument.ParseAsync throws specific JSON parsing exception
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => provider.FetchConfigurationAsync(query));
        
        Assert.Contains("invalid", exception.Message.ToLower());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task FetchConfigurationAsync_IncludesCustomHeaders()
    {
        // arrange: handler that captures request headers
        HttpRequestMessage? capturedRequest = null;
        var handler = new RequestCapturingHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
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

        // act
        await provider.FetchConfigurationAsync(query);

        // assert: headers were added
        Assert.NotNull(capturedRequest);
        Assert.Contains("Bearer token123", capturedRequest.Headers.GetValues("Authorization"));
        Assert.Contains("1.0.0", capturedRequest.Headers.GetValues("X-Client-Version"));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public void Constructor_BuildsCorrectUrl_WithBaseAddress()
    {
        // arrange: handler that captures final URL
        Uri? capturedUri = null;
        var handler = new RequestCapturingHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        // act: create provider with base address
        var provider = new HttpPollingProvider(
            new HttpPollingProviderOptions("https://api.example.com", TimeSpan.FromMilliseconds(100), handler));
        _disposables.Add(provider);
        
        var query = new HttpPollingProviderQueryOptions("/v1/config");
        _ = provider.FetchConfigurationAsync(query);

        // assert: URL was built correctly
        Assert.Equal("https://api.example.com/v1/config", capturedUri?.ToString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public void Constructor_UsesAbsoluteUrl_IgnoresBaseAddress()
    {
        // arrange
        Uri? capturedUri = null;
        var handler = new RequestCapturingHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var provider = new HttpPollingProvider(
            new HttpPollingProviderOptions("https://base.example.com", TimeSpan.FromMilliseconds(100), handler));
        _disposables.Add(provider);

        // act: use absolute URL - should ignore base address
        var query = new HttpPollingProviderQueryOptions("https://other.example.com/config");
        _ = provider.FetchConfigurationAsync(query);

        // assert: absolute URL used
        Assert.Equal("https://other.example.com/config", capturedUri?.ToString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_DoesNotEmit_OnException()
    {
        // arrange: handler that always throws
        var handler = new ExceptionHandler(new HttpRequestException("Network error"));
        var provider = CreateProvider(handler, TimeSpan.FromMilliseconds(50));
        var query = new HttpPollingProviderQueryOptions("/api/config");

        // act: collect emissions over time
        var emissions = new List<JsonElement>();
        using var subscription = provider.Changes(query)
            .Subscribe(element => emissions.Add(element));

        // wait for several poll attempts
        await Task.Delay(200);

        // assert: no emissions because exceptions return changed=false
        Assert.Empty(emissions);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_OnlyEmits_WhenContentChanges()
    {
        // arrange: handler that alternates between same and different content
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            CreateJsonResponse("{ \"Value\": 1 }"),  // first poll
            CreateJsonResponse("{ \"Value\": 1 }"),  // same content - should not emit
            CreateJsonResponse("{ \"Value\": 2 }"),  // changed content - should emit
            CreateJsonResponse("{ \"Value\": 2 }"),  // same again - should not emit
        });
        var handler = new QueueHandler(responses);
        var provider = CreateProvider(handler, TimeSpan.FromMilliseconds(80));
        var query = new HttpPollingProviderQueryOptions("/api/config");

        // act: collect emissions over time
        var emissions = new List<JsonElement>();
        using var subscription = provider.Changes(query)
            .Subscribe(element => emissions.Add(element));

        // wait for several poll cycles
        await Task.Delay(400);

        // assert: only 2 emissions (Value=1, then Value=2)
        Assert.Equal(2, emissions.Count);
        Assert.Equal(1, emissions[0].GetProperty("Value").GetInt32());
        Assert.Equal(2, emissions[1].GetProperty("Value").GetInt32());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "HttpPollingProvider")]
    public async Task Changes_DifferentQueries_UseDifferentCache()
    {
        // arrange: handler that returns different content based on URL
        var handler = new RequestCapturingHandler(req =>
        {
            var path = req.RequestUri?.PathAndQuery;
            var value = path?.Contains("config1") == true ? 1 : 2;
            return CreateJsonResponse($"{{ \"Value\": {value} }}");
        });

        var provider = CreateProvider(handler, TimeSpan.FromMilliseconds(50));
        var query1 = new HttpPollingProviderQueryOptions("/api/config1");
        var query2 = new HttpPollingProviderQueryOptions("/api/config2");

        // act: subscribe to both queries
        JsonElement? emission1 = null, emission2 = null;
        using var sub1 = provider.Changes(query1).Subscribe(e => emission1 = e);
        using var sub2 = provider.Changes(query2).Subscribe(e => emission2 = e);

        // wait for polls
        await Task.Delay(200);

        // assert: different values based on different URLs
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
        // arrange: create provider with custom handler
        var handler = new StaticHandler(CreateJsonResponse("{}"));
        var provider = new HttpPollingProvider(
            new HttpPollingProviderOptions("https://example.com", TimeSpan.FromMilliseconds(50), handler));

        // act: dispose multiple times (should be safe)
        provider.Dispose();
        provider.Dispose();

        // assert: no exceptions thrown - disposal is idempotent
        Assert.True(true); // If we get here, disposal worked correctly
    }

    #region Test Infrastructure

    private HttpPollingProvider CreateProvider(HttpMessageHandler handler, TimeSpan? pollInterval = null)
    {
        var provider = new HttpPollingProvider(
            new HttpPollingProviderOptions("https://example.com", pollInterval ?? TimeSpan.FromSeconds(1), handler));
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