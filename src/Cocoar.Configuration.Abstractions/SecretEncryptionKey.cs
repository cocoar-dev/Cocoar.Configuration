using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Secrets.SecretTypes;

/// <summary>
/// Canonical algorithm identifiers for the <c>cocoar.secret</c> hybrid encryption scheme.
/// A single source of truth shared by <see cref="SecretEnvelope{T}"/> and the published keys.
/// </summary>
public static class SecretAlgorithms
{
    /// <summary>Combined hybrid algorithm descriptor. <c>"RSA-OAEP-AES256-GCM"</c>.</summary>
    public const string Hybrid = "RSA-OAEP-AES256-GCM";

    /// <summary>RSA key-wrapping algorithm (OAEP with SHA-256). <c>"RSA-OAEP-256"</c>.</summary>
    public const string KeyWrap = "RSA-OAEP-256";

    /// <summary>Symmetric data-encryption algorithm. <c>"AES-256-GCM"</c>.</summary>
    public const string DataEncryption = "AES-256-GCM";
}

/// <summary>
/// The current public encryption key for one <c>kid</c> — the X.509 SubjectPublicKeyInfo a producer
/// (e.g. the <c>@cocoar/secrets</c> browser library) imports to build a matching <c>cocoar.secret</c>
/// envelope. Carries only public material; never a private key.
/// </summary>
public sealed record SecretEncryptionPublicKey
{
    /// <summary>Key identifier the producer stamps into the envelope <c>kid</c> field.</summary>
    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    /// <summary>Overall algorithm. <c>"RSA-OAEP-AES256-GCM"</c>.</summary>
    [JsonPropertyName("alg")]
    public string Alg { get; init; } = SecretAlgorithms.Hybrid;

    /// <summary>Key-wrapping algorithm. <c>"RSA-OAEP-256"</c>.</summary>
    [JsonPropertyName("walg")]
    public string Walg { get; init; } = SecretAlgorithms.KeyWrap;

    /// <summary>Data-encryption algorithm. <c>"AES-256-GCM"</c>.</summary>
    [JsonPropertyName("enc")]
    public string Enc { get; init; } = SecretAlgorithms.DataEncryption;

    /// <summary>Public-key structure. Always <c>"spki"</c> (X.509 SubjectPublicKeyInfo, DER).</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = "spki";

    /// <summary>Encoding of <see cref="PublicKey"/>. Always <c>"base64url"</c> (no padding).</summary>
    [JsonPropertyName("encoding")]
    public string Encoding { get; init; } = "base64url";

    /// <summary>The RSA public key as DER SubjectPublicKeyInfo, base64url-encoded WITHOUT padding.</summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }
}

/// <summary>
/// Publishes the public half of the configured secrets encryption key so external producers can
/// build <c>cocoar.secret</c> envelopes the server can later decrypt. Resolved from dependency injection.
/// <para>
/// There is exactly ONE current key per tenant — the newest certificate (per the configured comparer).
/// Implementations re-read key material on every call so certificate rotation is reflected. Public keys
/// are safe to expose; no private key or plaintext is ever reachable through this API. Each accessor
/// returns a SINGLE key — never a list — so one tenant's key can never expose another's.
/// </para>
/// </summary>
public interface ISecretEncryptionKeyProvider
{
    /// <summary>
    /// The current encryption public key for a single-tenant deployment (one configured kid), or
    /// <see langword="null"/> when nothing is publishable. For multi-tenant deployments use
    /// <see cref="GetCurrentKeyForTenant(string)"/> instead.
    /// </summary>
    SecretEncryptionPublicKey? GetCurrentKey();

    /// <summary>
    /// The current encryption public key for <paramref name="tenantId"/> (one per tenant), or
    /// <see langword="null"/> if that tenant has no publishable key. Returns exactly one key, so it
    /// can never expose another tenant's key.
    /// </summary>
    SecretEncryptionPublicKey? GetCurrentKeyForTenant(string tenantId);
}
