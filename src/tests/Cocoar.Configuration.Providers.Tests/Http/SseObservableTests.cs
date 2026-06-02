using System.Net;
using System.Text;
using Cocoar.Configuration.Http;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.Http;

/// <summary>
/// Tests for the SSE (Server-Sent Events) change path of <see cref="HttpProvider"/>, including the
/// read-idle timeout that reconnects a half-open (hung) connection.
/// </summary>
public class SseObservableTests
{
    private static HttpProviderQueryOptions Query => new("https://example.com/sse");

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Provider", "HttpProvider")]
    public async Task Sse_emits_bytes_from_a_data_line()
    {
        // A stream with a single SSE data line, then end-of-stream.
        var handler = new SseHandler(() =>
            new MemoryStream(Encoding.UTF8.GetBytes("data: {\"Value\":7}\n\n")));

        var provider = new HttpProvider(new HttpProviderOptions(serverSentEvents: true, handler: handler));

        var got = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = provider.ChangesAsBytes(Query)
            .Subscribe(bytes => got.TrySetResult(Encoding.UTF8.GetString(bytes)));

        var finished = await Task.WhenAny(got.Task, Task.Delay(3000)) == got.Task;

        Assert.True(finished, "SSE should emit the data-line payload");
        Assert.Equal("{\"Value\":7}", await got.Task);
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Provider", "HttpProvider")]
    public async Task Sse_read_idle_timeout_reconnects_a_hung_stream()
    {
        // A stream that sends headers then blocks forever — a half-open connection. Without the idle
        // timeout this would hang; with it, each read times out and RunAsync reconnects (new connection).
        var handler = new SseHandler(() => new BlockingStream());

        var provider = new HttpProvider(new HttpProviderOptions(
            serverSentEvents: true,
            handler: handler,
            sseReadIdleTimeout: TimeSpan.FromMilliseconds(150)));

        using var sub = provider.ChangesAsBytes(Query).Subscribe(_ => { });

        // First connect is immediate; idle timeout (150ms) + backoff (1s) → a reconnect within ~2.5s.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(3) && Volatile.Read(ref handler.Connections) < 2)
        {
            await Task.Delay(50);
        }

        Assert.True(Volatile.Read(ref handler.Connections) >= 2,
            $"hung SSE stream should reconnect; connections={handler.Connections}");
    }

    private sealed class SseHandler(Func<Stream> streamFactory) : HttpMessageHandler
    {
        public int Connections;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Connections);
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(streamFactory())
            };
            resp.Content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(resp);
        }
    }

    /// <summary>A readable stream whose reads block until cancelled — simulates a half-open connection.</summary>
    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task.ConfigureAwait(false); // never completes except via cancellation
        }
    }
}
