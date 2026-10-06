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
    private IReadOnlyList<string> _lastWarnings = [];

    /// <summary>
    /// Absolute ConfigHub delivery URL, for example <c>https://config.example/api/config/my-product/my-app</c>.
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

    /// <summary>
    /// Dimensions this client reports with every request, e.g. <c>server</c> → <c>APPDEV01</c>.
    /// ConfigHub uses them for the dimensions a product marks as client-reported. Part of the
    /// query identity: when they change (config-aware rules), the subscription is rebuilt.
    /// </summary>
    public IReadOnlyDictionary<string, string> Dimensions { get; }

    /// <summary>How warnings about the reported dimensions are treated.</summary>
    public ConfigHubWarningMode WarningMode { get; }

    /// <summary>Root configuration object selected by ConfigHub; part of the query identity.</summary>
    public string? ClassKey { get; }

    /// <summary>Optional callback invoked when the warnings ConfigHub returns change.</summary>
    [JsonIgnore]
    public Action<IReadOnlyList<string>>? OnWarnings { get; }

    internal SemaphoreSlim RefreshGate { get; } = new(1, 1);

    /// <summary>
    /// Creates a query for one ConfigHub delivery endpoint.
    /// </summary>
    /// <param name="url">Absolute HTTP or HTTPS delivery URL.</param>
    /// <param name="deliveryToken">Bearer token issued for the delivery endpoint.</param>
    /// <param name="dimensions">Optional dimensions this client reports (key → value).</param>
    /// <param name="warningMode">How warnings about the reported dimensions are treated.</param>
    /// <param name="onWarnings">Optional callback invoked when the returned warnings change.</param>
    /// <param name="classKey">Class object key; null requests the complete document for low-level clients.</param>
    public ConfigHubProviderQueryOptions(
        string url,
        string deliveryToken,
        IReadOnlyDictionary<string, string>? dimensions = null,
        ConfigHubWarningMode warningMode = ConfigHubWarningMode.Warn,
        Action<IReadOnlyList<string>>? onWarnings = null,
        string? classKey = null)
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
        Dimensions = NormalizeDimensions(dimensions);
        WarningMode = warningMode;
        OnWarnings = onWarnings;
        if (classKey is not null && (string.IsNullOrWhiteSpace(classKey) || classKey.Length > 200 || classKey.Any(char.IsControl)))
            throw new ArgumentException("Class key must contain 1–200 characters without control characters.", nameof(classKey));
        ClassKey = classKey;
    }

    /// <summary>The value of the <c>ConfigHub-Dimension</c> request header, or null without dimensions.</summary>
    internal string? DimensionHeaderValue => Dimensions.Count == 0
        ? null
        : string.Join(", ", Dimensions.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

    /// <summary>Stores the latest warnings; returns true when they differ from the previous ones.</summary>
    internal bool SetWarnings(IReadOnlyList<string> warnings)
    {
        lock (_validatorLock)
        {
            if (_lastWarnings.SequenceEqual(warnings, StringComparer.Ordinal))
            {
                return false;
            }

            _lastWarnings = warnings;
            return true;
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeDimensions(IReadOnlyDictionary<string, string>? dimensions)
    {
        // Sorted, so the serialized query identity does not depend on insertion order.
        var normalized = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in dimensions ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(key) || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                throw new ArgumentException(
                    $"Dimension key '{key}' is invalid; use letters, digits, '-', '_' or '.'.", nameof(dimensions));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue; // nothing to report for this dimension
            }

            normalized[key.Trim()] = value.Trim();
        }

        return normalized;
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
