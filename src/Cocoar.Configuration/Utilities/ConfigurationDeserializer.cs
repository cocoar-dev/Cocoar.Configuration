using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Utilities;


internal static class ConfigurationDeserializer
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();


    public static T? Deserialize<T>(JsonElement element) => element.Deserialize<T>(DefaultOptions);

    public static object? Deserialize(JsonElement element, Type type) => element.Deserialize(type, DefaultOptions);

    public static object? Deserialize(JsonElement element, Type type, IReadOnlyDictionary<Type, Type>? deserializationMap)
    {
        if (deserializationMap == null || deserializationMap.Count == 0)
        {
            return Deserialize(element, type);
        }

        var options = CreateOptionsWithInterfaceMapping(deserializationMap);
        return element.Deserialize(type, options);
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new StringToPrimitiveConverter<bool>());
        options.Converters.Add(new StringToPrimitiveConverter<int>());
        options.Converters.Add(new StringToPrimitiveConverter<double>());
        options.Converters.Add(new StringToPrimitiveConverter<float>());
        options.Converters.Add(new StringToPrimitiveConverter<long>());
        options.Converters.Add(new StringToPrimitiveConverter<DateTime>());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    private static JsonSerializerOptions CreateOptionsWithInterfaceMapping(IReadOnlyDictionary<Type, Type> deserializationMap)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new StringToPrimitiveConverter<bool>());
        options.Converters.Add(new StringToPrimitiveConverter<int>());
        options.Converters.Add(new StringToPrimitiveConverter<double>());
        options.Converters.Add(new StringToPrimitiveConverter<float>());
        options.Converters.Add(new StringToPrimitiveConverter<long>());
        options.Converters.Add(new StringToPrimitiveConverter<DateTime>());
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new InterfaceConverter(new Dictionary<Type, Type>(deserializationMap)));

        return options;
    }
}
