using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Cocoar.Configuration.Secrets;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// The JSON Schema of one configuration class as ConfigHub gets it, derived from the type alone:
/// what can be bound, written the way every source writes it.
/// </summary>
internal static class ConfigHubSchema
{
    // Settings are described as they are bound: property names unchanged, enums by name.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { KeepBindableProperties } },
        Converters = { new JsonStringEnumConverter() },
    };

    public static JsonNode Export(Type type)
    {
        var schema = SecretJsonSchema.Export(type, Options);
        SplitNullableEnums(schema);
        return schema;
    }

    // The exporter lists null as an enum member. ConfigHub reads nullability from a null type
    // instead, so a nullable enum is written as "these names, or null".
    private static void SplitNullableEnums(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array) SplitNullableEnums(item);
            return;
        }

        if (node is not JsonObject schema) return;

        if (schema["enum"] is JsonArray members && members.Any(member => member is null))
        {
            var names = new JsonArray([.. members.Where(member => member is not null).Select(member => member!.DeepClone())]);
            schema.Remove("enum");
            schema["anyOf"] = new JsonArray(new JsonObject { ["enum"] = names }, new JsonObject { ["type"] = "null" });
            return;
        }

        foreach (var child in schema.Select(property => property.Value).ToArray()) SplitNullableEnums(child);
    }

    // A computed property can never be set, so an editor must not offer it.
    private static void KeepBindableProperties(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;

        var constructorParameters = info.Type.GetConstructors()
            .SelectMany(c => c.GetParameters()).Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = info.Properties.Count - 1; i >= 0; i--)
        {
            var property = info.Properties[i];
            var bindable = property.Set is not null
                || constructorParameters.Contains(property.Name)
                || property.ObjectCreationHandling == JsonObjectCreationHandling.Populate;
            if (!bindable) info.Properties.RemoveAt(i);
        }
    }
}
