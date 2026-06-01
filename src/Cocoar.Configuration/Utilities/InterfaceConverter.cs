using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Utilities;

internal class InterfaceConverter : JsonConverterFactory
{
    private readonly Dictionary<Type, Type> _interfaceToConcreteMap;

    public InterfaceConverter(Dictionary<Type, Type> interfaceToConcreteMap)
    {
        _interfaceToConcreteMap = interfaceToConcreteMap;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsInterface && _interfaceToConcreteMap.ContainsKey(typeToConvert);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (_interfaceToConcreteMap.TryGetValue(typeToConvert, out var concreteType))
        {
            var converterType = typeof(InterfaceConverterInner<,>).MakeGenericType(typeToConvert, concreteType);
            return (JsonConverter?)Activator.CreateInstance(converterType);
        }

        return null;
    }

    private class InterfaceConverterInner<TInterface, TConcrete> : JsonConverter<TInterface>
        where TConcrete : TInterface
    {
        public override TInterface? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<TConcrete>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, TInterface value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
