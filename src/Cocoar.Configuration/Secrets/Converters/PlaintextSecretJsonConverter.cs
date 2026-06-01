using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Converters;

/// <summary>
/// Converter that serializes Secret&lt;T&gt; as its primitive value.
/// Used only in test scenarios to preserve secret values through FromStatic serialization.
/// </summary>
internal sealed class PlaintextSecretJsonConverter<T> : JsonConverter<Secret<T>>
{
    public override Secret<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Not used for reading - normal converter handles deserialization
        throw new NotSupportedException("Use standard SecretJsonConverter for reading");
    }

    public override void Write(Utf8JsonWriter writer, Secret<T> value, JsonSerializerOptions options)
    {
        // Open the secret and write its primitive value
        using var lease = value.Open();
        JsonSerializer.Serialize(writer, lease.Value, options);
    }
}

/// <summary>
/// Converter that serializes ISecret&lt;T&gt; as its primitive value.
/// Used only in test scenarios to preserve secret values through FromStatic serialization.
/// </summary>
internal sealed class PlaintextISecretJsonConverter<T> : JsonConverter<ISecret<T>>
{
    public override ISecret<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Not used for reading - normal converter handles deserialization
        throw new NotSupportedException("Use standard SecretJsonConverter for reading");
    }

    public override void Write(Utf8JsonWriter writer, ISecret<T> value, JsonSerializerOptions options)
    {
        // Open the secret and write its primitive value
        using var lease = value.Open();
        JsonSerializer.Serialize(writer, lease.Value, options);
    }
}
