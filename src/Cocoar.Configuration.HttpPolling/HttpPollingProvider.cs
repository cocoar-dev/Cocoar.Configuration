using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Utilities;
using System.Threading;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProvider(HttpPollingProviderOptions options)
    : ConfigurationProvider<HttpPollingProviderOptions, HttpPollingProviderQueryOptions>(options), IDisposable
{
    private readonly HttpClient _client = CreateClient(options);
    // Providers are dumb: no content cache/dedup here; emit every poll and let RuleManager dedup
    private bool _disposed;
    private int _consecutiveFailures;

    private static HttpClient CreateClient(HttpPollingProviderOptions opts)
    {
        HttpClient client;
        if (opts.Handler is not null)
        {
            client = new(opts.Handler, disposeHandler: false);
        }
        else
        {
            client = new();
        }
        
        if (!string.IsNullOrWhiteSpace(opts.BaseAddress))
        {
            client.BaseAddress = new(opts.BaseAddress);
        }
        
        return client;
    }

    public override async Task<byte[]> FetchConfigurationBytesAsync(HttpPollingProviderQueryOptions query, CancellationToken ct = default)
    {
        var url = BuildUrl(_client, query.UrlPathOrAbsolute);
        
        // Create timeout token linked to caller's cancellation token
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProviderOptions.PollInterval);
        
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (query.Headers != null)
        {
            foreach (var kv in query.Headers)
            {
                // Try headers first, if invalid as header, set as request property is not supported; skip invalid
                if (!req.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                {
                    // fallback: content headers not applicable for GET without content
                }
            }
        }
        var resp = await _client.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
        return bytes;
    }

    public override IObservable<byte[]> ChangesAsBytes(HttpPollingProviderQueryOptions query)
    {
        // Poll and emit on each interval; RuleManager will dedup identical payloads
        return Observable
            .Interval(ProviderOptions.PollInterval)
            .SelectMany(async _ =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(ProviderOptions.PollInterval);
                    var url = BuildUrl(_client, query.UrlPathOrAbsolute);
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (query.Headers != null)
                    {
                        foreach (var kv in query.Headers)
                        {
                            if (!req.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                            {
                                // ignore invalid header name/value
                            }
                        }
                    }
                    var resp = await _client.SendAsync(req, cts.Token).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                    // reset failure count on success
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    return (ok: true, value: bytes);
                }
                catch
                {
                    // On consecutive failures reaching threshold, emit a sentinel to trigger recompute/health update
                    var failures = Interlocked.Increment(ref _consecutiveFailures);
                    if (failures >= ProviderOptions.ErrorConsecutiveFailureThreshold)
                    {
                        // reset to avoid spamming; will emit again only after another threshold worth of failures
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        return (ok: true, value: Array.Empty<byte>());
                    }
                    // otherwise suppress emission on error
                    return (ok: false, value: Array.Empty<byte>());
                }
            })
            .Where(t => t.ok)
            .Select(t => t.value)
            .Publish()
            .RefCount();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Safety.DisposeQuietly(_client);
        // no finalizer; nothing to suppress
    }

    private static string BuildUrl(HttpClient client, string pathOrAbsolute)
    {
        // If it's a complete URL with scheme, use it as-is
        if (Uri.TryCreate(pathOrAbsolute, UriKind.Absolute, out var absoluteUri) && 
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri.ToString();
        }
        
        // If we have a base address, combine it with the path
        if (client.BaseAddress is not null)
        {
            return new Uri(client.BaseAddress, pathOrAbsolute).ToString();
        }
        
        // Otherwise, return the path as-is
        return pathOrAbsolute;
    }

    // No provider-level content cache or equality comparer
}
