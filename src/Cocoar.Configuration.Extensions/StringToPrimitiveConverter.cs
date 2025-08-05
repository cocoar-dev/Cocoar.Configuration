using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Extensions;

public class StringToPrimitiveConverter<T> : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (typeToConvert == typeof(bool) && bool.TryParse(str, out var b))
                return (T)(object)b;
            if (typeToConvert == typeof(int) && int.TryParse(str, out var i))
                return (T)(object)i;
            if (typeToConvert == typeof(double) && double.TryParse(str, out var d))
                return (T)(object)d;
            if (typeToConvert == typeof(float) && float.TryParse(str, out var f))
                return (T)(object)f;
            if (typeToConvert == typeof(long) && long.TryParse(str, out var l))
                return (T)(object)l;
            if (typeToConvert == typeof(DateTime) && DateTime.TryParse(str, out var dt))
                return (T)(object)dt;
            // fallback: try to parse as T
            return (T)Convert.ChangeType(str, typeToConvert);
        }
        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
        {
            if (typeToConvert == typeof(bool))
                return (T)(object)reader.GetBoolean();
        }
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (typeToConvert == typeof(int))
                return (T)(object)reader.GetInt32();
            if (typeToConvert == typeof(long))
                return (T)(object)reader.GetInt64();
            if (typeToConvert == typeof(float))
                return (T)(object)reader.GetSingle();
            if (typeToConvert == typeof(double))
                return (T)(object)reader.GetDouble();
            // fallback: try to parse as T
            var str = reader.GetDouble().ToString();
            return (T)Convert.ChangeType(str, typeToConvert);
        }
        if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }
        if (reader.TokenType == JsonTokenType.Null)
            return default;
        throw new JsonException($"Unsupported token type: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
