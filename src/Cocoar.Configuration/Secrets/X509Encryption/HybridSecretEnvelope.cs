using System.Text.Json.Serialization;

namespace Cocoar.Configuration.X509Encryption;

/// <summary>
/// Hybrid RSA+AES encrypted envelope.
/// Contains encrypted data and the AES key wrapped with RSA.
/// Uses short property names to match Cocoar.Configuration.Secrets format.
/// </summary>
public sealed record HybridSecretEnvelope
{
    /// <summary>
    /// The AES-256 key, encrypted with RSA-OAEP-SHA256 (base64-encoded).
    /// </summary>
    [JsonPropertyName("wk")]
    public required string WrappedKey { get; init; }

    /// <summary>
    /// Algorithm used to wrap the key (always "RSA-OAEP-256").
    /// </summary>
    [JsonPropertyName("walg")]
    public required string WrappingAlgorithm { get; init; }

    /// <summary>
    /// AES-GCM initialization vector (12 bytes, base64-encoded).
    /// </summary>
    [JsonPropertyName("iv")]
    public required string Iv { get; init; }

    /// <summary>
    /// AES-GCM encrypted ciphertext (base64-encoded).
    /// </summary>
    [JsonPropertyName("ct")]
    public required string Ciphertext { get; init; }

    /// <summary>
    /// AES-GCM authentication tag (16 bytes, base64-encoded).
    /// </summary>
    [JsonPropertyName("tag")]
    public required string Tag { get; init; }
}
