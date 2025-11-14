using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Secrets.Converters;

internal sealed class SecretJsonConverterFactory : JsonConverterFactory
{
    private readonly ConfigManagerCapabilityScope _scope;

    public SecretJsonConverterFactory(ConfigManagerCapabilityScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
            return false;

        var genericTypeDef = typeToConvert.GetGenericTypeDefinition();
        return genericTypeDef == typeof(SecretTypes.Secret<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(SecretJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter?)Activator.CreateInstance(converterType, _scope);
    }
}
