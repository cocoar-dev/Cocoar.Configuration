using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// JSON options for reading and writing WritableStore overlay values.
/// <para>
/// These are deliberately <em>vanilla</em>: the configuration pipeline's options carry custom converters
/// (notably <c>StringToPrimitiveConverter&lt;T&gt;</c>, whose <c>Write</c> re-enters with the same options and
/// stack-overflows on bare primitives). For overlay writes we need only enum-as-string round-tripping so the
/// values match what the case-insensitive pipeline deserializer reads back.
/// </para>
/// </summary>
internal static class OverlaySerialization
{
    /// <summary>Options for serializing an override value to a sparse leaf (no pipeline converters — Trap A).</summary>
    internal static readonly JsonSerializerOptions WriteOptions = CreateOptions();

    /// <summary>Options for deserializing the sparse overlay back into a partial T (case-insensitive keys).</summary>
    internal static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>
    /// Serializes a typed override value to a <see cref="JsonNode"/>. A <see langword="null"/> reference
    /// produces a <see langword="null"/> node, which the overlay persists as an explicit JSON null.
    /// </summary>
    internal static JsonNode? SerializeValue<TValue>(TValue value)
        => JsonSerializer.SerializeToNode(value, WriteOptions);

    /// <summary>Non-generic overload for runtime-typed values (used by the batch-patch adapter).</summary>
    internal static JsonNode? SerializeValue(object? value, Type valueType)
        => JsonSerializer.SerializeToNode(value, valueType, WriteOptions);
}
