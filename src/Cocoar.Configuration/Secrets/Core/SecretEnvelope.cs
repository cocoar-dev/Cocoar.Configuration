using System.Text.Json;

namespace Cocoar.Configuration.Secrets.Core;

internal sealed class SecretEnvelopeWrapper
{
    // Core required fields present on every Cocoar secret envelope
    public string Type { get; }
    public int Version { get; }
    public string? Alg { get; }
    public string Kid { get; }
    public DateTimeOffset? CreatedAt { get; }
    public JsonElement Data { get; }

    public SecretEnvelopeWrapper(string type, int version, string? alg, string kid, JsonElement data, DateTimeOffset? createdAt)
    {
        Type = type;
        Version = version;
        Alg = alg;
        Kid = kid;
        Data = data;
        CreatedAt = createdAt;
    }

    public static bool IsEnvelope(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Required discriminator fields
        if (!element.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!element.TryGetProperty("version", out var versionProp) || versionProp.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var type = typeProp.GetString();
        var version = versionProp.GetInt32();

        return string.Equals(type, "cocoar.secret", StringComparison.OrdinalIgnoreCase) && version == 1;
    }

    public static bool TryParse(JsonElement element, out SecretEnvelopeWrapper? envelope)
    {
        envelope = null;
        if (!IsEnvelope(element)) return false;

        string GetStr(string name)
            => element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()!
                : throw new FormatException($"Missing or invalid '{name}' in secret envelope");

        string? GetOptionalStr(string name)
            => element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;

        var type = GetStr("type");

        if (!element.TryGetProperty("version", out var versionProp) || versionProp.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException("Missing or invalid 'version' in secret envelope");
        }

        var version = versionProp.GetInt32();
        var alg = GetOptionalStr("alg");
        var kid = GetStr("kid");

        DateTimeOffset? createdAt = null;
        if (element.TryGetProperty("createdAt", out var ts) && ts.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(ts.GetString(), out var dto))
            {
                createdAt = dto;
            }
        }

        // Everything except the well-known metadata properties is treated as data
        var dataObject = new Dictionary<string, JsonElement>();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name == "type" || prop.Name == "version" || prop.Name == "alg" || prop.Name == "kid" || prop.Name == "createdAt")
                continue;

            dataObject[prop.Name] = prop.Value;
        }

        var dataJson = JsonSerializer.Serialize(dataObject);
        var dataElement = JsonDocument.Parse(dataJson).RootElement;

        envelope = new SecretEnvelopeWrapper(type, version, alg, kid, dataElement, createdAt);
        return true;
    }
}
