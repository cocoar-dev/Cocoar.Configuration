using System.Diagnostics;
using System.Text;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Http;

/// <summary>
/// Server-Sent Events (SSE) observable that opens a streaming HTTP connection
/// and emits configuration bytes whenever a <c>data:</c> event arrives.
/// Reconnects automatically on disconnect with exponential backoff.
/// If <see cref="HttpProviderOptions.FallbackPollInterval"/> is set, falls back
/// to polling when SSE connections fail repeatedly.
/// </summary>
internal sealed class SseObservable(HttpProvider provider, HttpProviderQueryOptions query) : IObservable<byte[]>
{
    // SSE reconnect backoff: starts at 1s, doubles up to 30s
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    // After this many consecutive SSE connection failures, fall back to polling (if configured)
    private const int SseFallbackThreshold = 5;

    public IDisposable Subscribe(IObserver<byte[]> observer)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(observer, cts.Token));
        return new HttpProvider.SubscriptionDisposable(cts);
    }

    private async Task RunAsync(IObserver<byte[]> observer, CancellationToken ct)
    {
        var consecutiveSseFailures = 0;
        var reconnectDelay = InitialReconnectDelay;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReadSseAsync(observer, ct).ConfigureAwait(false);

                // If ConnectAndReadSseAsync returns normally (stream ended), reset backoff
                // but count it as a failure for fallback purposes
                consecutiveSseFailures++;
                reconnectDelay = InitialReconnectDelay;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                consecutiveSseFailures++;
                Trace.TraceWarning(
                    "HttpProvider SSE: connection failed for '{0}': {1}: {2}. " +
                    "Reconnecting in {3:F1}s (failure #{4}).",
                    query.Url, ex.GetType().Name, ex.Message,
                    reconnectDelay.TotalSeconds, consecutiveSseFailures);
            }

            // Check whether to fall back to polling
            if (ShouldFallbackToPolling(consecutiveSseFailures))
            {
                Trace.TraceWarning(
                    "HttpProvider SSE: {0} consecutive SSE failures for '{1}'. " +
                    "Falling back to polling at {2}s interval.",
                    consecutiveSseFailures, query.Url,
                    provider.Options.FallbackPollInterval!.Value.TotalSeconds);

                await RunPollingFallbackAsync(observer, ct).ConfigureAwait(false);
                return;
            }

            // Wait before reconnecting with exponential backoff
            try
            {
                await Task.Delay(reconnectDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            reconnectDelay = TimeSpan.FromTicks(Math.Min(
                reconnectDelay.Ticks * 2,
                MaxReconnectDelay.Ticks));
        }
    }

    private async Task ConnectAndReadSseAsync(IObserver<byte[]> observer, CancellationToken ct)
    {
        // One client for this whole connection (service-backed: a fresh pooled-handler client per reconnect).
        var client = provider.AcquireClient();
        var url = HttpProvider.BuildUrl(client, query.Url);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new("text/event-stream"));
        HttpProvider.ApplyHeaders(req, query.Headers);

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Reset failure counters on successful connection
        provider.ResetFailureCount();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
            {
                // End of stream — server closed the connection
                return;
            }

            // SSE format: lines starting with "data: " contain the payload.
            // Empty lines delimit events. We emit on each data line for simplicity,
            // since our events are single-line JSON payloads.
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var json = line.AsSpan(6); // skip "data: "
                if (json.Length > 0)
                {
                    var bytes = Encoding.UTF8.GetBytes(json.ToString());
                    provider.ResetFailureCount();
                    Safety.NotifyQuietly(observer, bytes);
                }
            }
            // Ignore comment lines (starting with ':'), event type lines, id lines, etc.
        }
    }

    private bool ShouldFallbackToPolling(int consecutiveSseFailures)
    {
        return provider.Options.FallbackPollInterval.HasValue &&
               consecutiveSseFailures >= SseFallbackThreshold;
    }

    private async Task RunPollingFallbackAsync(IObserver<byte[]> observer, CancellationToken ct)
    {
        var interval = provider.Options.FallbackPollInterval!.Value;
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    pollCts.CancelAfter(interval);
                    var bytes = await provider.FetchAsync(query, pollCts.Token).ConfigureAwait(false);
                    provider.ResetFailureCount();
                    Safety.NotifyQuietly(observer, bytes);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    HttpProvider.HandleFetchFailure(provider, query, observer, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }
}
