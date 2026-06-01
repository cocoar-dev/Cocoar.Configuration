using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Capabilities;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Extensibility;

namespace Cocoar.Configuration.Utilities;


internal static class ConfigurationDeserializer
{
    public static T? Deserialize<T>(JsonElement element, ConfigManagerCapabilityScope? capabilityScope = null) 
        => element.Deserialize<T>(CreateOptions(capabilityScope));

    public static object? Deserialize(JsonElement element, Type type, ConfigManagerCapabilityScope? capabilityScope = null) 
        => element.Deserialize(type, CreateOptions(capabilityScope));

    public static object? Deserialize(JsonElement element, Type type, IReadOnlyDictionary<Type, Type>? deserializationMap, ConfigManagerCapabilityScope? capabilityScope = null)
    {
        if (deserializationMap == null || deserializationMap.Count == 0)
        {
            return Deserialize(element, type, capabilityScope);
        }

        var options = CreateOptionsWithInterfaceMapping(deserializationMap, capabilityScope);
        return element.Deserialize(type, options);
    }

    private static JsonSerializerOptions CreateOptions(ConfigManagerCapabilityScope? capabilityScope)
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

        ApplySerializerCapabilities(options, capabilityScope);

        return options;
    }

    private static JsonSerializerOptions CreateOptionsWithInterfaceMapping(IReadOnlyDictionary<Type, Type> deserializationMap, ConfigManagerCapabilityScope? capabilityScope)
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

        ApplySerializerCapabilities(options, capabilityScope);

        return options;
    }

    private static void ApplySerializerCapabilities(JsonSerializerOptions options, ConfigManagerCapabilityScope? capabilityScope)
    {
        capabilityScope?.Owner.GetComposition()?.UsingEach<ISerializerSetupCapability>(c => c.Configure(options));
    }
}
