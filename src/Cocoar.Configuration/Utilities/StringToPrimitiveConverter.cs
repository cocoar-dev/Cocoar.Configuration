using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Utilities;

public class StringToPrimitiveConverter<T> : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (str is null)
            {
                return default;
            }

            var target = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            if (target == typeof(bool) && bool.TryParse(str, out var b))
            {
                return (T?)(object)b;
            }

            if (target == typeof(int) && int.TryParse(str, out var i))
            {
                return (T?)(object)i;
            }

            if (target == typeof(double) && double.TryParse(str, out var d))
            {
                return (T?)(object)d;
            }

            if (target == typeof(float) && float.TryParse(str, out var f))
            {
                return (T?)(object)f;
            }

            if (target == typeof(long) && long.TryParse(str, out var l))
            {
                return (T?)(object)l;
            }

            if (target == typeof(DateTime) && DateTime.TryParse(str, out var dt))
            {
                return (T?)(object)dt;
            }

            var converted = Convert.ChangeType(str, target, System.Globalization.CultureInfo.InvariantCulture);
            return (T?)converted;
        }
        if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
        {
            var target = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            if (target == typeof(bool))
            {
                return (T?)(object)reader.GetBoolean();
            }
        }
        if (reader.TokenType == JsonTokenType.Number)
        {
            var target = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            if (target == typeof(int))
            {
                return (T?)(object)reader.GetInt32();
            }

            if (target == typeof(long))
            {
                return (T?)(object)reader.GetInt64();
            }

            if (target == typeof(float))
            {
                return (T?)(object)reader.GetSingle();
            }

            if (target == typeof(double))
            {
                return (T?)(object)reader.GetDouble();
            }

            var dbl = reader.GetDouble();
            var converted = Convert.ChangeType(dbl, target, System.Globalization.CultureInfo.InvariantCulture);
            return (T?)converted;
        }
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        throw new JsonException($"Unsupported token type: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
