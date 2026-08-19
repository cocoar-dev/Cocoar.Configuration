using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// Loads authoritative JSON snapshots over HTTP and uses SSE only as an invalidation channel.
/// </summary>
public sealed class ConfigHubProvider
    : ConfigurationProvider<ConfigHubProviderOptions, ConfigHubProviderQueryOptions>, IDisposable
{
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    /// <summary>
    /// Creates a provider with the specified connection-level options.
    /// </summary>
    /// <param name="options">Connection-level polling, timeout, and transport options.</param>
    public ConfigHubProvider(ConfigHubProviderOptions options)
        : base(options ?? throw new ArgumentNullException(nameof(options)))
    {
        _client = options.Handler is null
            ? new HttpClient()
            : new HttpClient(options.Handler, disposeHandler: false);
    }

    /// <inheritdoc />
    public override async Task<byte[]> FetchConfigurationBytesAsync(
        ConfigHubProviderQueryOptions query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var bytes = await FetchSnapshotAsync(query, useCacheValidator: false, ct).ConfigureAwait(false);
        return bytes ?? throw new InvalidOperationException(
            "ConfigHub returned no content for an unconditional snapshot request.");
    }

    /// <inheritdoc />
    public override IObservable<byte[]> ChangesAsBytes(ConfigHubProviderQueryOptions query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ChangeObservable(this, query);
    }

    /// <summary>
    /// Stops active subscriptions and disposes provider-owned HTTP resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _client.Dispose();
        _lifetime.Dispose();
    }

    private async Task<byte[]?> FetchSnapshotAsync(
        ConfigHubProviderQueryOptions query,
        bool useCacheValidator,
        CancellationToken ct)
    {
        await query.RefreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var request = CreateRequest(query, "application/json");

            var entityTag = useCacheValidator ? query.GetEntityTag(query.Url) : null;
            if (entityTag is not null && EntityTagHeaderValue.TryParse(entityTag, out var parsedEntityTag))
            {
                request.Headers.IfNoneMatch.Add(parsedEntityTag);
            }

            using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (response.Headers.ETag is { } refreshedEntityTag)
                {
                    query.SetEntityTag(query.Url, refreshedEntityTag.ToString());
                }

                return null;
            }

            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            query.SetEntityTag(query.Url, response.Headers.ETag?.ToString());
            return bytes;
        }
        finally
        {
            query.RefreshGate.Release();
        }
    }

    private static HttpRequestMessage CreateRequest(ConfigHubProviderQueryOptions query, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, query.Url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", query.DeliveryToken);
        return request;
    }

    private sealed class ChangeObservable(ConfigHubProvider provider, ConfigHubProviderQueryOptions query)
        : IObservable<byte[]>
    {
        public IDisposable Subscribe(IObserver<byte[]> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(provider._lifetime.Token);
            var sink = new SerializedObserver(observer);
            _ = Task.Run(() => RunAsync(sink, cts.Token), CancellationToken.None);
            return new Subscription(cts);
        }

        private async Task RunAsync(SerializedObserver observer, CancellationToken ct)
        {
            var tasks = new List<Task>
            {
                RunSseLoopAsync(observer, ct),
            };

            if (provider.ProviderOptions.FallbackPollInterval is { } interval)
            {
                tasks.Add(RunPollingLoopAsync(observer, interval, ct));
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                observer.Error(ex);
            }
        }

        private async Task RunSseLoopAsync(SerializedObserver observer, CancellationToken ct)
        {
            var reconnectDelay = InitialReconnectDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndReadAsync(observer, ct).ConfigureAwait(false);
                    reconnectDelay = InitialReconnectDelay;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(
                        "ConfigHub SSE connection to '{0}' failed: {1}: {2}. Retrying in {3:F1}s.",
                        query.Url,
                        ex.GetType().Name,
                        ex.Message,
                        reconnectDelay.TotalSeconds);
                }

                await Task.Delay(reconnectDelay, ct).ConfigureAwait(false);
                reconnectDelay = TimeSpan.FromTicks(Math.Min(
                    reconnectDelay.Ticks * 2,
                    MaxReconnectDelay.Ticks));
            }
        }

        private async Task ConnectAndReadAsync(SerializedObserver observer, CancellationToken ct)
        {
            using var request = ConfigHubProvider.CreateRequest(query, "text/event-stream");
            using var response = await provider._client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // The stream never owns configuration state. This reconciliation closes the gap left by
            // events that may have been published while the connection was down.
            await FetchAndEmitIfChangedAsync(observer, ct).ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var hasDataField = false;

            while (!ct.IsCancellationRequested)
            {
                var line = await ReadLineAsync(reader, ct).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                if (line.Length == 0)
                {
                    if (hasDataField)
                    {
                        await FetchAndEmitIfChangedAsync(observer, ct).ConfigureAwait(false);
                        hasDataField = false;
                    }

                    continue;
                }

                if (line[0] == ':')
                {
                    continue;
                }

                var colonIndex = line.IndexOf(':');
                var field = colonIndex >= 0 ? line[..colonIndex] : line;
                if (string.Equals(field, "data", StringComparison.Ordinal))
                {
                    hasDataField = true;
                }
            }
        }

        private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
        {
            if (provider.ProviderOptions.SseReadIdleTimeout is not { } timeout)
            {
                return await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(timeout);
            try
            {
                return await reader.ReadLineAsync(readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"ConfigHub SSE connection received no data for {timeout.TotalSeconds:F0} seconds.");
            }
        }

        private async Task RunPollingLoopAsync(
            SerializedObserver observer,
            TimeSpan interval,
            CancellationToken ct)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await FetchAndEmitIfChangedAsync(observer, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(
                        "ConfigHub fallback poll for '{0}' failed: {1}: {2}.",
                        query.Url,
                        ex.GetType().Name,
                        ex.Message);
                }
            }
        }

        private async Task FetchAndEmitIfChangedAsync(SerializedObserver observer, CancellationToken ct)
        {
            var bytes = await provider.FetchSnapshotAsync(query, useCacheValidator: true, ct)
                .ConfigureAwait(false);
            if (bytes is not null)
            {
                observer.Next(bytes);
            }
        }
    }

    private sealed class SerializedObserver(IObserver<byte[]> observer)
    {
        private readonly Lock _gate = new();
        private bool _stopped;

        public void Next(byte[] value)
        {
            lock (_gate)
            {
                if (!_stopped)
                {
                    observer.OnNext(value);
                }
            }
        }

        public void Error(Exception error)
        {
            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                observer.OnError(error);
            }
        }
    }

    private sealed class Subscription(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
