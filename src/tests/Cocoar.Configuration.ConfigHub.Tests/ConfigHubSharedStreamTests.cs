using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.ConfigHub.Tests;

/// <summary>
/// Invalidations are scoped to the access, not to a class: every rule reading from one delivery URL
/// listens on the same SSE connection, while its snapshot requests stay its own.
/// </summary>
public sealed class ConfigHubSharedStreamTests
{
    private const string Url = "https://config.example/api/config/shop/app";

    public sealed class A { public int Version { get; set; } }
    public sealed class B { public int Version { get; set; } }
    public sealed class C { public int Version { get; set; } }

    [Fact]
    public async Task ThirtyRulesOnOneUrl_OpenExactlyOneStream()
    {
        using var hub = new Hub();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: hub));
        var subscriptions = Enumerable.Range(0, 30)
            .Select(i => provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token", classKey: $"Class{i}")).Subscribe(new Sink()))
            .ToList();

        await hub.WaitForStreamsAsync(1);
        await hub.WaitForSnapshotsAsync(30);

        Assert.Equal(1, hub.StreamsOpened);
        var stream = Assert.Single(hub.StreamRequests);
        Assert.Equal("token", stream.Token);
        Assert.Null(stream.ClassKey);
        subscriptions.ForEach(s => s.Dispose());
    }

    [Fact]
    public async Task ThreeUrls_OpenThreeStreams()
    {
        using var hub = new Hub();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: hub));
        string[] urls = [Url, "https://config.example/api/config/shop/other", "https://second.example/api/config/shop/app"];
        var subscriptions = urls
            .SelectMany(url => new[] { "A", "B" }.Select(key => new ConfigHubProviderQueryOptions(url, "token", classKey: key)))
            .Select(query => provider.ChangesAsBytes(query).Subscribe(new Sink()))
            .ToList();

        await hub.WaitForStreamsAsync(3);

        Assert.Equal(urls.Order(), hub.StreamRequests.Select(r => r.Url).Order());
        subscriptions.ForEach(s => s.Dispose());
    }

    [Fact]
    public async Task Invalidation_ReachesEveryRuleOfTheUrl_WithItsOwnClassAndDimensions()
    {
        using var hub = new Hub();
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<A>().FromConfigHub(new ConfigHubRuleOptions(Url, "token", handler: hub).WithDimension("server", "one")),
            rule.For<B>().FromConfigHub(new ConfigHubRuleOptions(Url, "token", handler: hub)),
            rule.For<C>().FromConfigHub(new ConfigHubRuleOptions(Url, "token", handler: hub)),
        ]).UseDebounce(25));
        await hub.WaitForStreamsAsync(1);
        Assert.Equal(1, manager.GetConfig<A>()!.Version);

        hub.Publish(version: 2);

        await WaitUntilAsync(() => manager.GetConfig<A>()!.Version == 2 && manager.GetConfig<B>()!.Version == 2 && manager.GetConfig<C>()!.Version == 2);
        Assert.Equal(1, hub.StreamsOpened);
        Assert.Contains(hub.SnapshotRequests, r => r.ClassKey == "A" && r.Dimensions == "server=one");
        Assert.Contains(hub.SnapshotRequests, r => r.ClassKey == "B" && r.Dimensions is null);
    }

    [Fact]
    public async Task Reconnect_ReconcilesEveryRule()
    {
        using var hub = new Hub();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: hub));
        var a = new Sink();
        var b = new Sink();
        using var first = provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token", classKey: "A")).Subscribe(a);
        using var second = provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token", classKey: "B")).Subscribe(b);
        await hub.WaitForStreamsAsync(1);
        await WaitUntilAsync(() => a.Count == 1 && b.Count == 1);

        // The change happens while the connection is down, so no invalidation is ever sent for it.
        hub.SetVersion(2);
        hub.DropStreams();

        await hub.WaitForStreamsAsync(2);
        await WaitUntilAsync(() => a.Count == 2 && b.Count == 2);
    }

    [Fact]
    public async Task LastSubscriberLeaving_ClosesTheStream_AndANewOneReopensIt()
    {
        using var hub = new Hub();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: hub));
        var first = provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token", classKey: "A")).Subscribe(new Sink());
        var second = provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token", classKey: "B")).Subscribe(new Sink());
        await hub.WaitForStreamsAsync(1);

        first.Dispose();
        Assert.Equal(1, hub.StreamsActive);

        second.Dispose();
        await WaitUntilAsync(() => hub.StreamsActive == 0);

        using var third = provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token", classKey: "A")).Subscribe(new Sink());
        await hub.WaitForStreamsAsync(2);
    }

    [Fact]
    public async Task ProviderDisposal_ClosesTheStream()
    {
        using var hub = new Hub();
        var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: hub));
        provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token", classKey: "A")).Subscribe(new Sink());
        await hub.WaitForStreamsAsync(1);

        provider.Dispose();

        await WaitUntilAsync(() => hub.StreamsActive == 0);
    }

    [Fact]
    public async Task DifferentTokensForOneUrl_NeverShareAConnection()
    {
        using var hub = new Hub();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: hub));
        using var first = provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token-one", classKey: "A")).Subscribe(new Sink());
        using var second = provider.ChangesAsBytes(new ConfigHubProviderQueryOptions(Url, "token-two", classKey: "B")).Subscribe(new Sink());

        await hub.WaitForStreamsAsync(2);

        Assert.Equal(["token-one", "token-two"], hub.StreamRequests.Select(r => r.Token).Order());
        await hub.WaitForSnapshotsAsync(2);
        Assert.Contains(hub.SnapshotRequests, r => r.ClassKey == "A" && r.Token == "token-one");
        Assert.Contains(hub.SnapshotRequests, r => r.ClassKey == "B" && r.Token == "token-two");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out waiting for the condition.");
            await Task.Delay(20);
        }
    }

    private sealed class Sink : IObserver<byte[]>
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void OnNext(byte[] value) => Interlocked.Increment(ref _count);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private sealed record Seen(string Url, string? Token, string? ClassKey, string? Dimensions);

    /// <summary>A ConfigHub stand-in: versioned snapshots with ETags, and SSE streams it can publish to or drop.</summary>
    private sealed class Hub : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<Channel<string>, byte> _streams = new();
        private int _version = 1;
        private int _streamsOpened;

        public ConcurrentQueue<Seen> StreamRequests { get; } = new();
        public ConcurrentQueue<Seen> SnapshotRequests { get; } = new();
        public int StreamsOpened => Volatile.Read(ref _streamsOpened);
        public int StreamsActive => _streams.Count;

        public void SetVersion(int version) => Volatile.Write(ref _version, version);

        public void Publish(int version)
        {
            SetVersion(version);
            foreach (var stream in _streams.Keys) stream.Writer.TryWrite("event: config-changed\ndata: {}\n\n");
        }

        public void DropStreams()
        {
            foreach (var stream in _streams.Keys) stream.Writer.TryComplete();
        }

        public Task WaitForStreamsAsync(int opened) => WaitUntilAsync(() => StreamsOpened >= opened);
        public Task WaitForSnapshotsAsync(int count) => WaitUntilAsync(() => SnapshotRequests.Count >= count);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var seen = new Seen(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("ConfigHub-Class", out var keys) ? Uri.UnescapeDataString(keys.Single()) : null,
                request.Headers.TryGetValues(ConfigHubProvider.DimensionHeader, out var dimensions) ? dimensions.Single() : null);

            if (request.Headers.Accept.Any(value => value.MediaType == "text/event-stream"))
            {
                StreamRequests.Enqueue(seen);
                var channel = Channel.CreateUnbounded<string>();
                _streams[channel] = 0;
                Interlocked.Increment(ref _streamsOpened);
                var content = new StreamContent(new ChannelStream(channel, () => _streams.TryRemove(channel, out _)));
                content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }

            SnapshotRequests.Enqueue(seen);
            var version = Volatile.Read(ref _version);
            var entityTag = new EntityTagHeaderValue($"\"v{version}\"");
            var response = request.Headers.IfNoneMatch.Contains(entityTag)
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"Version":{{version}}}""", Encoding.UTF8, "application/json") };
            response.Headers.ETag = entityTag;
            return Task.FromResult(response);
        }
    }

    private sealed class ChannelStream(Channel<string> channel, Action closed) : Stream
    {
        private byte[] _pending = [];
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset == _pending.Length)
            {
                if (!await channel.Reader.WaitToReadAsync(cancellationToken) || !channel.Reader.TryRead(out var text))
                {
                    return 0;
                }

                _pending = Encoding.UTF8.GetBytes(text);
                _offset = 0;
            }

            var count = Math.Min(buffer.Length, _pending.Length - _offset);
            _pending.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        protected override void Dispose(bool disposing)
        {
            closed();
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
