using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Utilities;

internal sealed class ConfigurationCollectionConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (typeToConvert.IsArray)
        {
            return typeToConvert.GetArrayRank() == 1 && typeToConvert != typeof(byte[]);
        }

        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(List<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.IsArray
            ? typeToConvert.GetElementType()!
            : typeToConvert.GetGenericArguments()[0];
        var converterType = typeToConvert.IsArray
            ? typeof(IndexedArrayConverter<>).MakeGenericType(elementType)
            : typeof(IndexedListConverter<>).MakeGenericType(elementType);

        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class IndexedArrayConverter<TElement> : JsonConverter<TElement[]>
    {
        public override TElement[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadItems<TElement>(ref reader, options).ToArray();

        public override void Write(Utf8JsonWriter writer, TElement[] value, JsonSerializerOptions options)
            => WriteItems(writer, value, options);
    }

    private sealed class IndexedListConverter<TElement> : JsonConverter<List<TElement>>
    {
        public override List<TElement> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadItems<TElement>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, List<TElement> value, JsonSerializerOptions options)
            => WriteItems(writer, value, options);
    }

    private static List<TElement> ReadItems<TElement>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return ReadArray<TElement>(ref reader, options);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return ReadIndexedObject<TElement>(document.RootElement, options);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var json = reader.GetString();
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new JsonException("A collection string must contain a JSON array.");
            }

            return ReadJsonArrayString<TElement>(json, options);
        }

        throw new JsonException(
            $"A configuration collection must be a JSON array, an indexed object, or a string containing a JSON array; found {reader.TokenType}.");
    }

    private static List<TElement> ReadJsonArrayString<TElement>(string json, JsonSerializerOptions options)
    {
        try
        {
            return ParseJsonArray<TElement>(json, options);
        }
        catch (JsonException directException)
        {
            // MutableJson preserves the escaped lexical form of strings containing quotes. Decode that one
            // retained JSON-string layer so provider values such as ["value"] remain usable as JSON arrays.
            if (TryDecodePreservedStringEscapes(json, out var decodedJson))
            {
                try
                {
                    return ParseJsonArray<TElement>(decodedJson, options);
                }
                catch (JsonException)
                {
                    // Report the original parse failure because it best describes the supplied value.
                }
            }

            throw new JsonException(
                $"A collection string must contain a valid JSON array. {directException.Message}",
                directException);
        }
    }

    private static List<TElement> ParseJsonArray<TElement>(string json, JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A collection string must contain a JSON array.");
        }

        return ReadArrayElement<TElement>(document.RootElement, options);
    }

    private static bool TryDecodePreservedStringEscapes(string value, out string decodedValue)
    {
        try
        {
            decodedValue = JsonSerializer.Deserialize<string>($"\"{value}\"") ?? string.Empty;
            return decodedValue.Length > 0 && !string.Equals(decodedValue, value, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            decodedValue = string.Empty;
            return false;
        }
    }

    private static List<TElement> ReadArray<TElement>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var items = new List<TElement>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return items;
            }

            items.Add(JsonSerializer.Deserialize<TElement>(ref reader, options)!);
        }

        throw new JsonException("The JSON array is incomplete.");
    }

    private static List<TElement> ReadArrayElement<TElement>(JsonElement array, JsonSerializerOptions options)
    {
        var items = new List<TElement>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(element.Deserialize<TElement>(options)!);
        }

        return items;
    }

    private static List<TElement> ReadIndexedObject<TElement>(JsonElement indexedObject, JsonSerializerOptions options)
    {
        var items = new List<TElement>();
        foreach (var property in indexedObject.EnumerateObject().OrderBy(
                     property => property.Name,
                     ConfigurationKeySegmentComparer.Instance))
        {
            items.Add(property.Value.Deserialize<TElement>(options)!);
        }

        return items;
    }

    private static void WriteItems<TElement>(
        Utf8JsonWriter writer,
        IEnumerable<TElement> values,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            JsonSerializer.Serialize(writer, value, options);
        }

        writer.WriteEndArray();
    }

    private sealed class ConfigurationKeySegmentComparer : IComparer<string>
    {
        public static ConfigurationKeySegmentComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            var xIsInteger = int.TryParse(x, out var xInteger);
            var yIsInteger = int.TryParse(y, out var yInteger);

            if (xIsInteger && yIsInteger)
            {
                var numericComparison = xInteger.CompareTo(yInteger);
                return numericComparison != 0
                    ? numericComparison
                    : StringComparer.OrdinalIgnoreCase.Compare(x, y);
            }

            if (xIsInteger != yIsInteger)
            {
                return xIsInteger ? -1 : 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}
