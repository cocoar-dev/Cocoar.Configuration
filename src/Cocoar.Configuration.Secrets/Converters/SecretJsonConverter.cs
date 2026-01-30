using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Converters;

/// <summary>
/// JSON converter for ISecret&lt;T&gt; interface types.
/// Deserializes to Secret&lt;T&gt; instances, enabling interface-typed properties in configuration classes.
/// </summary>
internal sealed class ISecretJsonConverter<T> : JsonConverter<ISecret<T>>
{
    private readonly SecretJsonConverter<T> _innerConverter;

    public ISecretJsonConverter(ConfigManagerCapabilityScope scope)
    {
        _innerConverter = new SecretJsonConverter<T>(scope);
    }

    public override ISecret<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return _innerConverter.Read(ref reader, typeof(Secret<T>), options);
    }

    public override void Write(Utf8JsonWriter writer, ISecret<T> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue("***");
    }
}

internal sealed class SecretJsonConverter<T> : JsonConverter<Secret<T>>
{
    private readonly ConfigManagerCapabilityScope _scope;

    public SecretJsonConverter(ConfigManagerCapabilityScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    private bool GetAllowPlaintextSetting()
    {
        var composition = _scope.Owner.GetComposition();
        var policies = composition?.GetAll<SecretsPolicy>().ToList();
        var policy = policies is { Count: > 0 } ? policies[^1] : SecretsPolicy.Default;
        return policy.AllowPlaintextSecrets;
    }

    public override Secret<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var resolver = new SecretsDecryptorResolver(_scope);

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var element = doc.RootElement;
            
            if (SecretEnvelopeWrapper.IsEnvelope(element))
            {
                if (!SecretEnvelopeWrapper.TryParse(element, out var env) || env is null)
                {
                    throw new JsonException($"Invalid secret envelope for Secret<{typeof(T).Name}>");
                }
                
                return new Secret<T>(env, resolver);
            }
            
            var plainValue = JsonSerializer.Deserialize<T>(element.GetRawText(), options);
            if (plainValue is null)
            {
                throw new JsonException($"Failed to deserialize plain value for Secret<{typeof(T).Name}>");
            }
            return new Secret<T>(plainValue, resolver, allowPlaintext: GetAllowPlaintextSetting());
        }

        if (reader.TokenType == JsonTokenType.String ||
            reader.TokenType == JsonTokenType.Number ||
            reader.TokenType == JsonTokenType.True ||
            reader.TokenType == JsonTokenType.False ||
            reader.TokenType == JsonTokenType.Null)
        {
            using var tempDoc = JsonDocument.ParseValue(ref reader);
            var plainValue = JsonSerializer.Deserialize<T>(tempDoc.RootElement.GetRawText(), options);
            if (plainValue is null)
            {
                throw new JsonException($"Failed to deserialize plain value for Secret<{typeof(T).Name}>");
            }
            return new Secret<T>(plainValue, resolver, allowPlaintext: GetAllowPlaintextSetting());
        }

        throw new JsonException($"Unexpected token type '{reader.TokenType}' for Secret<{typeof(T).Name}>");
    }

    public override void Write(Utf8JsonWriter writer, Secret<T> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue("***");
    }
}
