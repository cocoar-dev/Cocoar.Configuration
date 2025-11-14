namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Internal helper for base64url encoding/decoding of HybridEnvelope data.
/// </summary>
internal static class HybridEnvelopeSerializer
{
    /// <summary>
    /// Decode a base64url-encoded string to bytes.
    /// </summary>
    internal static byte[] FromBase64Url(string s)
    {
        if (string.IsNullOrEmpty(s))
            return Array.Empty<byte>();

        var base64 = s.Replace('-', '+').Replace('_', '/');
        var pad = base64.Length % 4;
        if (pad != 0)
            base64 = base64.PadRight(base64.Length + (4 - pad), '=');

        return Convert.FromBase64String(base64);
    }
}
