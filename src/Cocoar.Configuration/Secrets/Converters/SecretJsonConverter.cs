using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Converters;

/// <summary>
/// Shared helper for determining if a type can hold null values.
/// Used by both SecretJsonConverter and ISecretJsonConverter.
/// </summary>
internal static class SecretNullabilityHelper
{
    /// <summary>
    /// Returns true if type T can legally hold a null value.
    /// This includes reference types (string?, object?) and nullable value types (int?, bool?).
    /// </summary>
    public static bool TypeAcceptsNull<T>()
    {
        // Reference types where default is null
        if (!typeof(T).IsValueType)
            return true;
        // Nullable<T> value types (int?, bool?, etc.)
        return Nullable.GetUnderlyingType(typeof(T)) != null;
    }
}

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

    /// <summary>
    /// Always handle null JSON values - delegate to inner converter for proper error handling.
    /// </summary>
    public override bool HandleNull => true;

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

    /// <summary>
    /// Always handle null JSON values so we can:
    /// - Create a Secret containing null for nullable types (T?)
    /// - Throw a clear error for non-nullable types (T)
    /// Without this, System.Text.Json silently sets the property to null.
    /// </summary>
    public override bool HandleNull => true;

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

            // Deserialize directly from JsonElement — never create an intermediate string.
            // .NET strings are immutable and cannot be zeroed; plaintext secrets must stay as bytes.
            var plainValue = element.Deserialize<T>(options);
            if (plainValue is null && !SecretNullabilityHelper.TypeAcceptsNull<T>())
            {
                throw new JsonException($"Failed to deserialize plain value for Secret<{typeof(T).Name}>");
            }
            return new Secret<T>(plainValue!, resolver, allowPlaintext: GetAllowPlaintextSetting());
        }

        // Handle explicit null token
        if (reader.TokenType == JsonTokenType.Null)
        {
            if (!SecretNullabilityHelper.TypeAcceptsNull<T>())
            {
                throw new JsonException(
                    $"Cannot deserialize null to Secret<{typeof(T).Name}>. " +
                    $"Use Secret<{typeof(T).Name}?> if the value can be null.");
            }
            // For nullable types, create a Secret containing null
            return new Secret<T>(default!, resolver, allowPlaintext: GetAllowPlaintextSetting());
        }

        if (reader.TokenType == JsonTokenType.String ||
            reader.TokenType == JsonTokenType.Number ||
            reader.TokenType == JsonTokenType.True ||
            reader.TokenType == JsonTokenType.False)
        {
            using var tempDoc = JsonDocument.ParseValue(ref reader);
            var plainValue = tempDoc.RootElement.Deserialize<T>(options);
            if (plainValue is null && !SecretNullabilityHelper.TypeAcceptsNull<T>())
            {
                throw new JsonException($"Failed to deserialize plain value for Secret<{typeof(T).Name}>");
            }
            return new Secret<T>(plainValue!, resolver, allowPlaintext: GetAllowPlaintextSetting());
        }

        throw new JsonException($"Unexpected token type '{reader.TokenType}' for Secret<{typeof(T).Name}>");
    }

    public override void Write(Utf8JsonWriter writer, Secret<T> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue("***");
    }
}
