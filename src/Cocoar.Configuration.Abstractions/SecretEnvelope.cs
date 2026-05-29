using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Secrets.SecretTypes;

/// <summary>
/// The wire / transport form of an encrypted secret — a <c>cocoar.secret</c> envelope produced
/// client-side (e.g. by the <c>@cocoar/secrets</c> browser library) or by the Secrets CLI.
/// <para>
/// This is NOT a usable secret: it is ciphertext to be stored, and it cannot be opened without the
/// private key. It is deliberately a plain, JSON-bindable record (so an API request DTO can carry it)
/// — it does <em>not</em> inherit from <c>Secret&lt;T&gt;</c>, which has a different role (an openable
/// runtime secret). The phantom <typeparamref name="T"/> couples the envelope to the value type of the
/// target <c>Secret&lt;T&gt;</c> so the compiler can match them at the write call site.
/// </para>
/// <para>
/// Binary fields (<see cref="Wk"/>, <see cref="Iv"/>, <see cref="Ct"/>, <see cref="Tag"/>) are
/// base64url WITHOUT padding — the encoding the decryption path requires.
/// </para>
/// </summary>
/// <typeparam name="T">The value type of the secret this envelope encrypts (phantom; for typing only).</typeparam>
public sealed record SecretEnvelope<T>
{
    /// <summary>Envelope discriminator. Always <c>"cocoar.secret"</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "cocoar.secret";

    /// <summary>Envelope format version. Always <c>1</c>.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    /// <summary>Key identifier — must match a decryption key configured on the server.</summary>
    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    /// <summary>Overall algorithm. <c>"RSA-OAEP-AES256-GCM"</c>.</summary>
    [JsonPropertyName("alg")]
    public string Alg { get; init; } = "RSA-OAEP-AES256-GCM";

    /// <summary>The AES-256 key wrapped with RSA-OAEP-SHA256 (base64url, no padding).</summary>
    [JsonPropertyName("wk")]
    public required string Wk { get; init; }

    /// <summary>Key-wrapping algorithm. <c>"RSA-OAEP-256"</c>.</summary>
    [JsonPropertyName("walg")]
    public string Walg { get; init; } = "RSA-OAEP-256";

    /// <summary>AES-GCM 96-bit initialization vector (base64url, no padding).</summary>
    [JsonPropertyName("iv")]
    public required string Iv { get; init; }

    /// <summary>AES-GCM ciphertext (base64url, no padding).</summary>
    [JsonPropertyName("ct")]
    public required string Ct { get; init; }

    /// <summary>AES-GCM 128-bit authentication tag (base64url, no padding).</summary>
    [JsonPropertyName("tag")]
    public required string Tag { get; init; }
}
