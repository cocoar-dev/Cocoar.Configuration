namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Encodes a DER SubjectPublicKeyInfo as base64url WITHOUT padding — the same codec the
/// <c>cocoar.secret</c> envelope wire format and the <c>@cocoar/secrets</c> browser library use.
/// </summary>
internal static class SpkiEncoding
{
    public static string ToBase64Url(byte[] spki)
        => Convert.ToBase64String(spki).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
