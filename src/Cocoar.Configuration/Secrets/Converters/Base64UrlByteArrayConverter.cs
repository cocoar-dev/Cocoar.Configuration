using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Secrets.Protectors.Hybrid;

namespace Cocoar.Configuration.Secrets.Converters;

/// <summary>
/// JSON converter for byte arrays that uses base64url encoding instead of standard base64.
/// This ensures URL-safe encoding without padding characters.
/// </summary>
internal sealed class Base64UrlByteArrayConverter : JsonConverter<byte[]>
{
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var base64Url = reader.GetString();
        if (string.IsNullOrEmpty(base64Url))
            return Array.Empty<byte>();

        return HybridEnvelopeSerializer.FromBase64Url(base64Url);
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        if (value == null || value.Length == 0)
        {
            writer.WriteStringValue(string.Empty);
            return;
        }

        var base64 = Convert.ToBase64String(value);
        var base64Url = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        writer.WriteStringValue(base64Url);
    }
}
