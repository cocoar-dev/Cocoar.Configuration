using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Secrets.Tests.CrossLang;

/// <summary>
/// Cross-language acceptance: an envelope produced by the <c>@cocoar/secrets</c> TypeScript client (committed
/// as a golden fixture) must decrypt on the .NET side to the original value. Proves the wire format is
/// byte-compatible across stacks — RSA-OAEP-SHA256 key wrap + AES-256-GCM, base64url (no padding), and a
/// JSON-serialized plaintext (a string becomes a quoted JSON string). The decryption mirrors
/// <c>CertificateInventory.TryDecryptWithCert</c>. Regenerate: <c>pnpm --filter @cocoar/secrets gen:fixtures</c>.
/// </summary>
[Trait("Category", "Secrets")]
[Trait("Type", "Unit")]
public class TsEnvelopeCrossLangTests
{
    private sealed record Fixture(
        [property: JsonPropertyName("plaintext")] string Plaintext,
        [property: JsonPropertyName("privateKeyPkcs8")] string PrivateKeyPkcs8,
        [property: JsonPropertyName("envelope")] Envelope Envelope);

    private sealed record Envelope(
        [property: JsonPropertyName("kid")] string Kid,
        [property: JsonPropertyName("wk")] string Wk,
        [property: JsonPropertyName("iv")] string Iv,
        [property: JsonPropertyName("ct")] string Ct,
        [property: JsonPropertyName("tag")] string Tag);

    [Fact]
    public void TypeScriptEnvelope_DecryptsOnDotNet_ToOriginalValue()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CrossLang", "ts-envelope.fixture.json");
        Assert.True(
            File.Exists(path),
            $"Cross-language fixture missing at {path}. Regenerate with: pnpm --filter @cocoar/secrets gen:fixtures");

        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path))
                      ?? throw new InvalidOperationException("Fixture could not be parsed.");
        var envelope = fixture.Envelope;

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(fixture.PrivateKeyPkcs8), out _);

        // 1. RSA-OAEP-SHA256 unwrap the one-time AES key.
        var dek = rsa.Decrypt(FromBase64Url(envelope.Wk), RSAEncryptionPadding.OaepSHA256);

        // 2. AES-256-GCM open (ciphertext + tag, 96-bit IV, no AAD) — exactly what the server does.
        var iv = FromBase64Url(envelope.Iv);
        var ciphertext = FromBase64Url(envelope.Ct);
        var tag = FromBase64Url(envelope.Tag);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(dek, tag.Length))
        {
            aes.Decrypt(iv, ciphertext, tag, plaintext);
        }

        // The plaintext is the value JSON-serialized; a string round-trips as a quoted JSON string.
        var value = JsonSerializer.Deserialize<string>(plaintext);
        Assert.Equal(fixture.Plaintext, value);
    }

    private static byte[] FromBase64Url(string value)
    {
        var b64 = value.Replace('-', '+').Replace('_', '/');
        b64 += (b64.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(b64);
    }
}
