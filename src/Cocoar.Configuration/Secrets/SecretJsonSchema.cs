using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets;

/// <summary>
/// JSON Schema export for configuration types that contain secrets. A <see cref="Secret{T}"/>
/// property is described by the schema of its plaintext <c>T</c> (a string, or an object) and
/// marked with <see cref="Marker"/>, so a configuration UI knows to encrypt what is entered and to
/// store only the <c>cocoar.secret</c> envelope.
/// </summary>
public static class SecretJsonSchema
{
    /// <summary>Schema keyword marking a secret property: <c>"x-cocoar-secret": true</c>.</summary>
    public const string Marker = "x-cocoar-secret";

    /// <summary>Exporter options with <see cref="MarkSecrets"/>; nullable only where annotated.</summary>
    public static JsonSchemaExporterOptions ExporterOptions { get; } = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = MarkSecrets,
    };

    /// <summary>The JSON Schema of <paramref name="type"/>, with secrets marked.</summary>
    public static JsonNode Export(Type type, JsonSerializerOptions options) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(options, type, ExporterOptions);

    /// <summary>
    /// <see cref="JsonSchemaExporterOptions.TransformSchemaNode"/> hook: replaces the schema of a
    /// <see cref="Secret{T}"/> (or <see cref="ISecret{T}"/>) with the marked schema of <c>T</c>.
    /// </summary>
    public static JsonNode MarkSecrets(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (PlaintextType(context.TypeInfo.Type) is not { } plaintext)
            return schema;
        if (JsonSchemaExporter.GetJsonSchemaAsNode(context.TypeInfo.Options, plaintext, ExporterOptions) is not JsonObject marked)
            return schema;

        if (AllowsNull(schema) && marked["type"] is JsonValue single && single.TryGetValue<string>(out var name))
            marked["type"] = new JsonArray(name, "null");
        marked[Marker] = true;
        return marked;
    }

    private static Type? PlaintextType(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Secret<>) || type.GetGenericTypeDefinition() == typeof(ISecret<>))
            ? type.GetGenericArguments()[0]
            : null;

    private static bool AllowsNull(JsonNode schema) =>
        schema["type"] is JsonArray types && types.Any(t => t is JsonValue v && v.TryGetValue<string>(out var s) && s == "null");
}
