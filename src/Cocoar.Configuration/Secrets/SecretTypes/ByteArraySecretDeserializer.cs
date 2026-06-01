using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;

namespace Cocoar.Configuration.Secrets.SecretTypes;

/// <summary>
/// Handles deserialization of Secret&lt;byte[]&gt; with special logic for:
/// - Base64-encoded strings (decoded to bytes)
/// - Plain strings (converted to UTF-8 bytes)
/// - Non-string JSON (returned as raw UTF-8 bytes)
/// </summary>
internal static class ByteArraySecretDeserializer
{
    /// <summary>
    /// Deserializes JSON bytes to byte[] with intelligent type detection.
    /// Keeps decrypted bytes in memory for minimum duration.
    /// </summary>
    /// <param name="jsonBytes">The JSON representation (already decrypted)</param>
    /// <param name="needsCleanup">Whether the input bytes should be cleaned up</param>
    /// <returns>A SecretLease containing the byte array value</returns>
    public static SecretLease<T> Deserialize<T>(byte[] jsonBytes, bool needsCleanup)
    {
        try
        {
            var reader = new Utf8JsonReader(jsonBytes, isFinalBlock: true, state: default);

            if (!reader.Read())
            {
                // Empty or invalid JSON - return raw bytes
                return CreateLease<T>(jsonBytes, needsCleanup, additionalCleanup: null);
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return HandleStringToken<T>(ref reader, jsonBytes, needsCleanup);
            }

            // Non-string JSON - return raw UTF-8 bytes
            return CreateLease<T>(jsonBytes, needsCleanup, additionalCleanup: null);
        }
        catch
        {
            // On any parsing error, return raw bytes
            return CreateLease<T>(jsonBytes, needsCleanup, additionalCleanup: null);
        }
    }

    private static SecretLease<T> HandleStringToken<T>(ref Utf8JsonReader reader, byte[] jsonBytes, bool needsCleanup)
    {
        // Extract string content bytes
        byte[] stringContentBytes;
        if (reader.HasValueSequence)
        {
            stringContentBytes = ExtractFromSequence(reader.ValueSequence);
        }
        else
        {
            stringContentBytes = reader.ValueSpan.ToArray();
        }

        // Try Base64 decode
        if (TryDecodeBase64(stringContentBytes, out var decoded, out var decodedLength))
        {
            // Successfully decoded as Base64
            if (decodedLength != decoded.Length)
            {
                Array.Resize(ref decoded, decodedLength);
            }

            return CreateLease<T>(decoded, needsCleanup, additionalCleanup: decoded);
        }

        // Not valid Base64, return string content as UTF-8 bytes
        return CreateLease<T>(stringContentBytes, needsCleanup, additionalCleanup: stringContentBytes);
    }

    private static byte[] ExtractFromSequence(ReadOnlySequence<byte> sequence)
    {
        var length = checked((int)sequence.Length);
        var bytes = new byte[length];
        var offset = 0;

        foreach (var segment in sequence)
        {
            segment.Span.CopyTo(bytes.AsSpan(offset));
            offset += segment.Length;
        }

        return bytes;
    }

    private static bool TryDecodeBase64(byte[] input, out byte[] decoded, out int decodedLength)
    {
        var maxLen = Base64.GetMaxDecodedFromUtf8Length(input.Length);
        decoded = new byte[maxLen];

        var status = Base64.DecodeFromUtf8(input, decoded, out var consumed, out decodedLength);

        return status == OperationStatus.Done && consumed == input.Length;
    }

    private static SecretLease<T> CreateLease<T>(byte[] value, bool needsCleanup, byte[]? additionalCleanup)
    {
        Action cleanup = () =>
        {
            if (needsCleanup)
            {
                Array.Clear(value, 0, value.Length);
            }

            if (additionalCleanup != null && !ReferenceEquals(additionalCleanup, value))
            {
                Array.Clear(additionalCleanup, 0, additionalCleanup.Length);
            }
        };

        return new SecretLease<T>((T)(object)value, cleanup);
    }
}
