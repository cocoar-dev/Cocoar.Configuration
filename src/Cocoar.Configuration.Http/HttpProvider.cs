using System.Diagnostics;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Reactive.Internal;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Http;

/// <summary>
/// HTTP configuration provider supporting one-time fetch, polling, and Server-Sent Events (SSE).
/// </summary>
public sealed class HttpProvider(HttpProviderOptions options)
    : ConfigurationProvider<HttpProviderOptions, HttpProviderQueryOptions>(options), IDisposable
{
    private readonly HttpClient _client = CreateClient(options);
    private bool _disposed;
    private int _consecutiveFailures;

    private static HttpClient CreateClient(HttpProviderOptions opts)
    {
        if (opts.Handler is not null)
        {
            return new(opts.Handler, disposeHandler: false);
        }

        return new();
    }

    public override async Task<byte[]> FetchConfigurationBytesAsync(HttpProviderQueryOptions query, CancellationToken ct = default)
    {
        return await FetchAsync(query, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns an observable based on the configured mode:
    /// <list type="bullet">
    ///   <item>No poll interval + no SSE: <c>Never</c> (one-time fetch only)</item>
    ///   <item>Poll interval set: <see cref="PollingObservable"/> (periodic polling)</item>
    ///   <item>SSE enabled: <see cref="SseObservable"/> (live updates via Server-Sent Events)</item>
    ///   <item>SSE + fallback poll interval: SSE with automatic polling fallback</item>
    /// </list>
    /// </summary>
    public override IObservable<byte[]> ChangesAsBytes(HttpProviderQueryOptions query)
    {
        if (ProviderOptions.ServerSentEvents)
        {
            return new SseObservable(this, query);
        }

        if (ProviderOptions.PollInterval is { } interval)
        {
            return new PollingObservable(this, query, interval);
        }

        // One-time fetch: no change tracking
        return ObservableHelpers.Never<byte[]>();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Safety.DisposeQuietly(_client);
    }

    /// <summary>
    /// Shared fetch logic used by both <see cref="FetchConfigurationBytesAsync"/> and the poll loop.
    /// </summary>
    internal async Task<byte[]> FetchAsync(HttpProviderQueryOptions query, CancellationToken ct)
    {
        var url = BuildUrl(_client, query.Url);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(req, query.Headers);

        var resp = await _client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    internal static void ApplyHeaders(HttpRequestMessage req, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var kv in headers)
        {
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
    }

    internal static string BuildUrl(HttpClient client, string pathOrAbsolute)
    {
        if (Uri.TryCreate(pathOrAbsolute, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri.ToString();
        }

        if (client.BaseAddress is not null)
        {
            return new Uri(client.BaseAddress, pathOrAbsolute).ToString();
        }

        return pathOrAbsolute;
    }

    internal void ResetFailureCount() => Interlocked.Exchange(ref _consecutiveFailures, 0);

    internal int IncrementFailureCount() => Interlocked.Increment(ref _consecutiveFailures);

    internal int FailureThreshold => ProviderOptions.ErrorConsecutiveFailureThreshold;

    internal HttpClient Client => _client;

    internal HttpProviderOptions Options => ProviderOptions;

    /// <summary>
    /// Polls an HTTP endpoint on a periodic interval and emits fetched bytes.
    /// </summary>
    internal sealed class PollingObservable(HttpProvider provider, HttpProviderQueryOptions query, TimeSpan interval) : IObservable<byte[]>
    {
        public IDisposable Subscribe(IObserver<byte[]> observer)
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoop(observer, cts.Token));
            return new SubscriptionDisposable(cts);
        }

        private async Task PollLoop(IObserver<byte[]> observer, CancellationToken ct)
        {
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
                        HandleFetchFailure(provider, query, observer, ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
        }
    }

    internal static void HandleFetchFailure(HttpProvider provider, HttpProviderQueryOptions query, IObserver<byte[]> observer, Exception ex)
    {
        var failures = provider.IncrementFailureCount();
        if (failures >= provider.FailureThreshold)
        {
            var url = BuildUrl(provider._client, query.Url);
            Trace.TraceWarning(
                "HttpProvider: {0} consecutive failures for '{1}' " +
                "(threshold: {2}). Last error: {3}: {4}. " +
                "Emitting empty config to trigger health degradation.",
                failures, url, provider.FailureThreshold,
                ex.GetType().Name, ex.Message);
            provider.ResetFailureCount();
            Safety.NotifyQuietly(observer, Array.Empty<byte>());
        }
    }

    internal sealed class SubscriptionDisposable(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            Safety.CancelAndDisposeQuietly(cts);
        }
    }
}
