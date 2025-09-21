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
        HttpClient client;
        if (opts.Handler is not null)
        {
            client = new HttpClient(opts.Handler, disposeHandler: false);
        }
        else
        {
            client = new HttpClient();
        }
        
        if (!string.IsNullOrWhiteSpace(opts.BaseAddress))
        {
            client.BaseAddress = new Uri(opts.BaseAddress);
        }
        
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
            // Use streaming hash comparison - much faster than string comparison
            return ComputeJsonElementHash(x) == ComputeJsonElementHash(y);
        }
        
        public int GetHashCode(JsonElement obj) => ComputeJsonElementHash(obj);

        private static int ComputeJsonElementHash(JsonElement element)
        {
            try
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var stream = new System.Security.Cryptography.CryptoStream(System.IO.Stream.Null, md5, System.Security.Cryptography.CryptoStreamMode.Write);
                using var writer = new System.Text.Json.Utf8JsonWriter(stream);
                
                element.WriteTo(writer);
                writer.Flush();
                stream.FlushFinalBlock();
                
                // Convert first 4 bytes of hash to int for GetHashCode
                var hash = md5.Hash!;
                return BitConverter.ToInt32(hash, 0);
            }
            catch
            {
                return element.GetRawText().GetHashCode();
            }
        }
    }
}
