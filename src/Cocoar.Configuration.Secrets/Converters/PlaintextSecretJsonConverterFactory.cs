using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Converters;

/// <summary>
/// Factory that creates plaintext secret converters for test scenarios.
/// These converters serialize Secret&lt;T&gt; and ISecret&lt;T&gt; as their primitive values
/// instead of "***".
/// </summary>
internal sealed class PlaintextSecretJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType) return false;
        var def = typeToConvert.GetGenericTypeDefinition();
        return def == typeof(Secret<>) || def == typeof(ISecret<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var genericTypeDef = typeToConvert.GetGenericTypeDefinition();
        var valueType = typeToConvert.GetGenericArguments()[0];

        if (genericTypeDef == typeof(ISecret<>))
        {
            var converterType = typeof(PlaintextISecretJsonConverter<>).MakeGenericType(valueType);
            return (JsonConverter?)Activator.CreateInstance(converterType);
        }

        var secretConverterType = typeof(PlaintextSecretJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter?)Activator.CreateInstance(secretConverterType);
    }
}
