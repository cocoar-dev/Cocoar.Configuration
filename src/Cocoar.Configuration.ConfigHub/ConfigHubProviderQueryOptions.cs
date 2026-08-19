using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// Identifies one ConfigHub delivery endpoint and its credential.
/// </summary>
public sealed class ConfigHubProviderQueryOptions : IProviderQuery
{
    private readonly Lock _validatorLock = new();
    private string? _validatorUrl;
    private string? _entityTag;

    /// <summary>
    /// Absolute ConfigHub delivery URL, for example <c>https://config.example/api/config/my-app</c>.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// A non-secret identity used by Cocoar.Configuration to notice token rotation and rebuild the subscription.
    /// </summary>
    public string CredentialFingerprint { get; }

    /// <summary>
    /// ConfigHub delivery token. It is deliberately excluded from query serialization and logging.
    /// </summary>
    [JsonIgnore]
    public string DeliveryToken { get; }

    internal SemaphoreSlim RefreshGate { get; } = new(1, 1);

    /// <summary>
    /// Creates a query for one ConfigHub delivery endpoint.
    /// </summary>
    /// <param name="url">Absolute HTTP or HTTPS delivery URL.</param>
    /// <param name="deliveryToken">Bearer token issued for the delivery endpoint.</param>
    public ConfigHubProviderQueryOptions(string url, string deliveryToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("An absolute HTTP or HTTPS URL is required.", nameof(url));
        }

        if (string.IsNullOrWhiteSpace(deliveryToken) || !IsValidBearerToken(deliveryToken))
        {
            throw new ArgumentException(
                "A valid ConfigHub delivery token is required.",
                nameof(deliveryToken));
        }

        Url = uri.ToString();
        DeliveryToken = deliveryToken;
        CredentialFingerprint = Fingerprint(deliveryToken);
    }

    internal string? GetEntityTag(string resolvedUrl)
    {
        lock (_validatorLock)
        {
            return string.Equals(_validatorUrl, resolvedUrl, StringComparison.Ordinal)
                ? _entityTag
                : null;
        }
    }

    internal void SetEntityTag(string resolvedUrl, string? entityTag)
    {
        lock (_validatorLock)
        {
            _validatorUrl = resolvedUrl;
            _entityTag = entityTag;
        }
    }

    private static string Fingerprint(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool IsValidBearerToken(string value)
        => AuthenticationHeaderValue.TryParse($"Bearer {value}", out var header) &&
           string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(header.Parameter, value, StringComparison.Ordinal);
}
