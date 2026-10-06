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
    /// <summary>Request header carrying the reported dimensions.</summary>
    public const string DimensionHeader = "ConfigHub-Dimension";

    /// <summary>Response header carrying warnings about the reported dimensions.</summary>
    public const string WarningHeader = "ConfigHub-Warning";

    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _streamsGate = new();
    private readonly Dictionary<string, SharedStream> _streams = new(StringComparer.Ordinal);
    private readonly HashSet<string> _conflictingUrls = new(StringComparer.Ordinal);
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
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
            {
                HandleWarnings(query, response);
            }

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
        if (query.ClassKey is { } classKey)
            request.Headers.TryAddWithoutValidation("ConfigHub-Class", Uri.EscapeDataString(classKey));
        if (query.DimensionHeaderValue is { } dimensions)
        {
            request.Headers.TryAddWithoutValidation(DimensionHeader, dimensions);
        }

        return request;
    }

    /// <summary>
    /// ConfigHub still delivers when a reported dimension is missing, unknown or not allowed —
    /// without the affected layer — and says so in a response header. Changes are logged and
    /// passed to the callback; in <see cref="ConfigHubWarningMode.Fail"/> the fetch fails.
    /// </summary>
    private static void HandleWarnings(ConfigHubProviderQueryOptions query, HttpResponseMessage response)
    {
        IReadOnlyList<string> warnings = response.Headers.TryGetValues(WarningHeader, out var values)
            ? values.SelectMany(v => v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList()
            : [];

        if (query.SetWarnings(warnings))
        {
            if (warnings.Count > 0)
            {
                Trace.TraceWarning("ConfigHub '{0}' reported: {1}.", query.Url, string.Join("; ", warnings));
            }

            try
            {
                query.OnWarnings?.Invoke(warnings);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("ConfigHub warning callback failed: {0}: {1}.", ex.GetType().Name, ex.Message);
            }
        }

        if (warnings.Count > 0 && query.WarningMode == ConfigHubWarningMode.Fail)
        {
            throw new ConfigHubWarningException(warnings);
        }
    }

    private sealed class ChangeObservable(ConfigHubProvider provider, ConfigHubProviderQueryOptions query)
        : IObservable<byte[]>
    {
        public IDisposable Subscribe(IObserver<byte[]> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(provider._lifetime.Token);
            var subscriber = new Subscriber(provider, query, new SerializedObserver(observer), cts.Token);
            provider.Attach(subscriber);

            if (provider.ProviderOptions.FallbackPollInterval is { } interval)
            {
                _ = Task.Run(() => subscriber.RunPollingLoopAsync(interval), CancellationToken.None);
            }

            return new Subscription(provider, subscriber, cts);
        }
    }

    private static string StreamKey(ConfigHubProviderQueryOptions query) => query.Url + "\n" + query.CredentialFingerprint;

    private void Attach(Subscriber subscriber)
    {
        var query = subscriber.Query;
        bool reconcileNow;
        lock (_streamsGate)
        {
            if (!_streams.TryGetValue(StreamKey(query), out var stream))
            {
                // A stream is never opened with another rule's token, so a second credential for the
                // same URL gets its own connection. That costs a stream and usually is a mistake.
                if (_streams.Values.Any(s => s.Url == query.Url) && _conflictingUrls.Add(query.Url))
                {
                    Trace.TraceWarning(
                        "ConfigHub delivery URL '{0}' is used with more than one delivery token; each token keeps its own SSE connection.",
                        query.Url);
                }

                stream = new SharedStream(this, query.Url, query.DeliveryToken);
                _streams[StreamKey(query)] = stream;
                stream.Start();
            }

            stream.Subscribers.Add(subscriber);
            reconcileNow = stream.Connected;
        }

        // A rule that joins an open stream missed that stream's reconciliation on connect.
        if (reconcileNow)
        {
            _ = subscriber.RefreshAsync();
        }
    }

    private void Detach(Subscriber subscriber)
    {
        lock (_streamsGate)
        {
            var key = StreamKey(subscriber.Query);
            if (!_streams.TryGetValue(key, out var stream) || !stream.Subscribers.Remove(subscriber) || stream.Subscribers.Count > 0)
            {
                return;
            }

            _streams.Remove(key);
            stream.Stop();
        }
    }

    /// <summary>
    /// One rule's interest in a delivery URL. The snapshot request stays per rule, because class and
    /// dimensions are part of it; only the invalidation signal is shared.
    /// </summary>
    private sealed class Subscriber(
        ConfigHubProvider provider,
        ConfigHubProviderQueryOptions query,
        SerializedObserver observer,
        CancellationToken ct)
    {
        private int _generation;

        public ConfigHubProviderQueryOptions Query => query;

        /// <summary>
        /// Refetches conditionally and retries with backoff until it succeeds. A newer invalidation
        /// takes over from a retry that is still waiting, so retries never pile up.
        /// </summary>
        public async Task RefreshAsync()
        {
            var generation = Interlocked.Increment(ref _generation);
            var retryDelay = InitialReconnectDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await FetchAndEmitIfChangedAsync().ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(
                        "ConfigHub snapshot refresh for '{0}' failed: {1}: {2}. Retrying in {3:F1}s.",
                        query.Url,
                        ex.GetType().Name,
                        ex.Message,
                        retryDelay.TotalSeconds);
                }

                try
                {
                    await Task.Delay(retryDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (generation != Volatile.Read(ref _generation))
                {
                    return;
                }

                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxReconnectDelay.Ticks));
            }
        }

        public async Task RunPollingLoopAsync(TimeSpan interval)
        {
            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        await FetchAndEmitIfChangedAsync().ConfigureAwait(false);
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        private async Task FetchAndEmitIfChangedAsync()
        {
            var bytes = await provider.FetchSnapshotAsync(query, useCacheValidator: true, ct)
                .ConfigureAwait(false);
            if (bytes is not null)
            {
                observer.Next(bytes);
            }
        }
    }

    /// <summary>
    /// The single SSE connection for one delivery URL and credential. Invalidations are scoped to the
    /// access and carry no class, so every rule reading from that URL listens on the same connection.
    /// </summary>
    private sealed class SharedStream(ConfigHubProvider provider, string url, string deliveryToken)
    {
        private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(provider._lifetime.Token);

        public string Url => url;

        // Guarded by the provider's stream gate.
        public List<Subscriber> Subscribers { get; } = [];
        public bool Connected { get; private set; }

        public void Start() => _ = Task.Run(RunAsync, CancellationToken.None);

        public void Stop()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task RunAsync()
        {
            var ct = _cts.Token;
            var reconnectDelay = InitialReconnectDelay;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await ConnectAndReadAsync(ct).ConfigureAwait(false);
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
                            url,
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                _cts.Dispose();
            }
        }

        private async Task ConnectAndReadAsync(CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deliveryToken);
            using var response = await provider._client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            try
            {
                // The stream never owns configuration state. This reconciliation closes the gap left by
                // events that may have been published while the connection was down.
                RefreshSubscribers(markConnected: true);

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
                            RefreshSubscribers(markConnected: false);
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
            finally
            {
                lock (provider._streamsGate)
                {
                    Connected = false;
                }
            }
        }

        private void RefreshSubscribers(bool markConnected)
        {
            Subscriber[] subscribers;
            lock (provider._streamsGate)
            {
                if (markConnected)
                {
                    Connected = true;
                }

                subscribers = [.. Subscribers];
            }

            // Each rule refetches on its own, so one failing class does not hold back the others
            // or the reading of the next invalidation.
            foreach (var subscriber in subscribers)
            {
                _ = subscriber.RefreshAsync();
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
    }

    private sealed class SerializedObserver(IObserver<byte[]> observer)
    {
        private readonly Lock _gate = new();

        // A fallback poll and an invalidation can finish at the same time.
        public void Next(byte[] value)
        {
            lock (_gate)
            {
                observer.OnNext(value);
            }
        }
    }

    private sealed class Subscription(ConfigHubProvider provider, Subscriber subscriber, CancellationTokenSource cts) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            provider.Detach(subscriber);
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