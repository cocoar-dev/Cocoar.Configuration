using System.Text.Json;

namespace Cocoar.Configuration.Utilities;

/// <summary>
/// Utility class for JSON deserialization with custom type converters.
/// </summary>
internal static class ConfigurationDeserializer
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

    /// <summary>
    /// Deserializes a JSON element to the specified type with custom converters.
    /// </summary>
    public static T? Deserialize<T>(JsonElement element)
    {
        return element.Deserialize<T>(DefaultOptions);
    }

    /// <summary>
    /// Deserializes a JSON element to the specified type with custom converters.
    /// </summary>
    public static object? Deserialize(JsonElement element, Type type)
    {
        return element.Deserialize(type, DefaultOptions);
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions();

        // Register converters for common primitives
        options.Converters.Add(new StringToPrimitiveConverter<bool>());
        options.Converters.Add(new StringToPrimitiveConverter<int>());
        options.Converters.Add(new StringToPrimitiveConverter<double>());
        options.Converters.Add(new StringToPrimitiveConverter<float>());
        options.Converters.Add(new StringToPrimitiveConverter<long>());
        options.Converters.Add(new StringToPrimitiveConverter<DateTime>());

        return options;
    }
}
