using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.HttpPolling;

public sealed class HttpPollingProvider(HttpPollingProviderOptions options)
    : ConfigurationProvider<HttpPollingProviderOptions, HttpPollingProviderQueryOptions>(options), IDisposable
{
    private readonly HttpClient _client = CreateClient(options);
    private readonly ConcurrentDictionary<string, JsonElement> _lastByKey = new();
    private bool _disposed;

    private static HttpClient CreateClient(HttpPollingProviderOptions opts)
    {
        if (opts.Handler is not null) return new HttpClient(opts.Handler, disposeHandler: false)
        {
            BaseAddress = string.IsNullOrWhiteSpace(opts.BaseAddress) ? null : new Uri(opts.BaseAddress)
        };
        var client = new HttpClient();
        if (!string.IsNullOrWhiteSpace(opts.BaseAddress)) client.BaseAddress = new Uri(opts.BaseAddress);
        return client;
    }

    public override async Task<JsonElement> FetchConfigurationAsync(HttpPollingProviderQueryOptions query, CancellationToken ct = default)
    {
        var key = MakeKey(query);
        if (_lastByKey.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var url = BuildUrl(_client, query.UrlPathOrAbsolute);
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
        var resp = await _client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var element = doc.RootElement.Clone();
    _lastByKey[key] = element;
    return element;
    }

    public override IObservable<JsonElement> Changes(HttpPollingProviderQueryOptions query)
    {
        // Poll and emit only when payload changes
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
                    await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
                    var element = doc.RootElement.Clone();
                    var key = MakeKey(query);
                    if (_lastByKey.TryGetValue(key, out var last))
                    {
                        if (JsonElementEqualityComparer.Instance.Equals(last, element))
                        {
                            return (changed: false, value: element);
                        }
                    }
                    _lastByKey[key] = element;
                    return (changed: true, value: element);
                }
                catch
                {
                    return (changed: false, value: JsonDocument.Parse("{}").RootElement);
                }
            })
            .Where(t => t.changed)
            .Select(t => t.value)
            .Publish()
            .RefCount();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _client.Dispose(); } catch { /* ignore */ }
    // no finalizer; nothing to suppress
    }

    private static string BuildUrl(HttpClient client, string pathOrAbsolute)
        => Uri.TryCreate(pathOrAbsolute, UriKind.Absolute, out var abs)
            ? abs.ToString()
            : client.BaseAddress is not null
                ? new Uri(client.BaseAddress, pathOrAbsolute).ToString()
                : pathOrAbsolute;

    private static string MakeKey(HttpPollingProviderQueryOptions query)
    {
        var hdr = query.Headers == null
            ? string.Empty
            : string.Join(";", query.Headers.OrderBy(k => k.Key).Select(kv => kv.Key + "=" + kv.Value));
    return $"{query.UrlPathOrAbsolute}|{hdr}";
    }

    private sealed class JsonElementEqualityComparer : IEqualityComparer<JsonElement>
    {
        public static readonly JsonElementEqualityComparer Instance = new();
        public bool Equals(JsonElement x, JsonElement y)
        {
            return JsonSerializer.Serialize(x) == JsonSerializer.Serialize(y);
        }
        public int GetHashCode(JsonElement obj) => JsonSerializer.Serialize(obj).GetHashCode();
    }
}
