using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Cocoar.Configuration.ConfigHub.Tests;

public sealed class ConfigHubProviderTests
{
    [Fact]
    public async Task InvalidationEventRefetchesSnapshotConditionallyAndIgnoresEventPayload()
    {
        var handler = new ProtocolHandler();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: handler));
        var query = new ConfigHubProviderQueryOptions(
            "https://config.example/api/config/orders",
            "chcfg_secret");

        var initial = await provider.FetchConfigurationBytesAsync(query, CancellationToken.None);
        Assert.Equal("{\"version\":1}", Encoding.UTF8.GetString(initial));

        var update = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = provider.ChangesAsBytes(query).Subscribe(new TestObserver(
            bytes => update.TrySetResult(Encoding.UTF8.GetString(bytes)),
            error => update.TrySetException(error)));

        var completed = await Task.WhenAny(
            update.Task,
            Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Same(update.Task, completed);
        Assert.Equal("{\"version\":2}", await update.Task);
        subscription.Dispose();
        Assert.Equal(3, handler.SnapshotRequests);
        Assert.All(handler.AuthorizationSchemes, scheme => Assert.Equal("Bearer", scheme));
        Assert.All(handler.AuthorizationParameters, token => Assert.Equal("chcfg_secret", token));
        Assert.Equal(["\"v1\"", "\"v1\""], handler.IfNoneMatchValues);
    }

    [Fact]
    public void QuerySerializationIdentityDoesNotExposeDeliveryToken()
    {
        var query = new ConfigHubProviderQueryOptions(
            "https://config.example/api/config/orders",
            "chcfg_do-not-serialize");

        var json = System.Text.Json.JsonSerializer.Serialize(query);

        Assert.DoesNotContain("chcfg_do-not-serialize", json, StringComparison.Ordinal);
        Assert.Contains(query.CredentialFingerprint, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FallbackPollRefreshesSnapshotWhileSseConnectionRemainsOpen()
    {
        using var handler = new ResilienceHandler();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(
            fallbackPollInterval: TimeSpan.FromMilliseconds(50),
            handler: handler));
        var query = new ConfigHubProviderQueryOptions(
            "https://config.example/api/config/orders",
            "chcfg_secret");

        await provider.FetchConfigurationBytesAsync(query, CancellationToken.None);

        var update = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = provider.ChangesAsBytes(query).Subscribe(new TestObserver(
            bytes => update.TrySetResult(Encoding.UTF8.GetString(bytes)),
            error => update.TrySetException(error)));

        var completed = await Task.WhenAny(update.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(update.Task, completed);
        Assert.Equal("{\"version\":2}", await update.Task);
        Assert.True(handler.SseRequests >= 1);
        Assert.True(handler.ConditionalSnapshotRequests >= 2);
    }

    [Fact]
    public async Task IdleSseReconnectReconcilesSnapshotBeforeReadingNextEvent()
    {
        using var handler = new ResilienceHandler();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(
            sseReadIdleTimeout: TimeSpan.FromMilliseconds(50),
            handler: handler));
        var query = new ConfigHubProviderQueryOptions(
            "https://config.example/api/config/orders",
            "chcfg_secret");

        await provider.FetchConfigurationBytesAsync(query, CancellationToken.None);

        var update = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = provider.ChangesAsBytes(query).Subscribe(new TestObserver(
            bytes => update.TrySetResult(Encoding.UTF8.GetString(bytes)),
            error => update.TrySetException(error)));

        var completed = await Task.WhenAny(update.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(update.Task, completed);
        Assert.Equal("{\"version\":2}", await update.Task);
        Assert.True(handler.SseRequests >= 2);
        Assert.True(handler.ConditionalSnapshotRequests >= 2);
    }

    private sealed class ProtocolHandler : HttpMessageHandler
    {
        private int _snapshotRequests;

        public int SnapshotRequests => Volatile.Read(ref _snapshotRequests);
        public List<string?> AuthorizationSchemes { get; } = [];
        public List<string?> AuthorizationParameters { get; } = [];
        public List<string> IfNoneMatchValues { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (AuthorizationSchemes)
            {
                AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
                AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            }

            if (request.Headers.Accept.Any(value => value.MediaType == "text/event-stream"))
            {
                var body = new MemoryStream(Encoding.UTF8.GetBytes(
                    "event: config-changed\n" +
                    "data: this payload is deliberately not JSON\n\n"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(body)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
                    },
                });
            }

            var requestNumber = Interlocked.Increment(ref _snapshotRequests);
            var validator = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            if (validator is not null)
            {
                lock (IfNoneMatchValues)
                {
                    IfNoneMatchValues.Add(validator);
                }
            }

            return Task.FromResult(requestNumber switch
            {
                1 => JsonResponse("{\"version\":1}", "\"v1\""),
                2 => NotModified("\"v1\""),
                _ => JsonResponse("{\"version\":2}", "\"v2\""),
            });
        }

        private static HttpResponseMessage JsonResponse(string json, string entityTag)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new EntityTagHeaderValue(entityTag);
            return response;
        }

        private static HttpResponseMessage NotModified(string entityTag)
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.ETag = new EntityTagHeaderValue(entityTag);
            return response;
        }
    }

    private sealed class TestObserver(Action<byte[]> onNext, Action<Exception> onError) : IObserver<byte[]>
    {
        public void OnNext(byte[] value) => onNext(value);
        public void OnError(Exception error) => onError(error);
        public void OnCompleted() { }
    }

    private sealed class ResilienceHandler : HttpMessageHandler
    {
        private int _conditionalSnapshotRequests;
        private int _sseRequests;

        public int ConditionalSnapshotRequests => Volatile.Read(ref _conditionalSnapshotRequests);
        public int SseRequests => Volatile.Read(ref _sseRequests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.Accept.Any(value => value.MediaType == "text/event-stream"))
            {
                Interlocked.Increment(ref _sseRequests);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new BlockingReadStream()),
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(response);
            }

            var validator = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            if (validator is null)
            {
                return Task.FromResult(CreateJsonResponse("{\"version\":1}", "\"v1\""));
            }

            var conditionalRequest = Interlocked.Increment(ref _conditionalSnapshotRequests);
            return Task.FromResult(conditionalRequest == 1
                ? CreateNotModifiedResponse("\"v1\"")
                : CreateJsonResponse("{\"version\":2}", "\"v2\""));
        }

        private static HttpResponseMessage CreateJsonResponse(string json, string entityTag)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new EntityTagHeaderValue(entityTag);
            return response;
        }

        private static HttpResponseMessage CreateNotModifiedResponse(string entityTag)
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.ETag = new EntityTagHeaderValue(entityTag);
            return response;
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
