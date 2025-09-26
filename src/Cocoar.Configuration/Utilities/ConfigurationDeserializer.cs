using System.Text.Json;

namespace Cocoar.Configuration.Utilities;


internal static class ConfigurationDeserializer
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();


    public static T? Deserialize<T>(JsonElement element) => element.Deserialize<T>(DefaultOptions);

    public static object? Deserialize(JsonElement element, Type type) => element.Deserialize(type, DefaultOptions);

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions();

        options.Converters.Add(new StringToPrimitiveConverter<bool>());
        options.Converters.Add(new StringToPrimitiveConverter<int>());
        options.Converters.Add(new StringToPrimitiveConverter<double>());
        options.Converters.Add(new StringToPrimitiveConverter<float>());
        options.Converters.Add(new StringToPrimitiveConverter<long>());
        options.Converters.Add(new StringToPrimitiveConverter<DateTime>());

        return options;
    }
}
