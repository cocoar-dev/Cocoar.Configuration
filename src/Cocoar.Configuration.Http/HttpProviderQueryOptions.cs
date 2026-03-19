using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Http;

/// <summary>
/// Query-level options for <see cref="HttpProvider"/> — the URL and headers for a specific configuration type.
/// </summary>
public sealed class HttpProviderQueryOptions : IProviderQuery
{
    /// <summary>
    /// URL path (relative to HttpClient base address) or absolute URL.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Optional headers sent with requests for this specific query.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public HttpProviderQueryOptions(
        string url,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Headers = headers;
    }
}
