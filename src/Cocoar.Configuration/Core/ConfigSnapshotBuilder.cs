using System.Globalization;
using System.Text;
using System.Text.Json;
using Cocoar.Capabilities;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Utilities;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Records a single deserialization failure during snapshot building.
/// </summary>
/// <param name="ConfigType">The configuration type that failed to deserialize.</param>
/// <param name="Message">A human-readable description of the failure.</param>
/// <param name="Exception">The underlying exception, if any.</param>
/// <param name="JsonPreview">A preview of the JSON that failed to deserialize (truncated for safety).</param>
public sealed record DeserializationFailure(
    Type ConfigType,
    string Message,
    Exception? Exception,
    string? JsonPreview);

/// <summary>
/// Exception thrown when one or more configuration types fail to deserialize during startup.
/// </summary>
public sealed class ConfigurationDeserializationException : Exception
{
    /// <summary>
    /// The list of deserialization failures that caused this exception.
    /// </summary>
    public IReadOnlyList<DeserializationFailure> Failures { get; }

    public ConfigurationDeserializationException(IReadOnlyList<DeserializationFailure> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    private static string BuildMessage(IReadOnlyList<DeserializationFailure> failures)
    {
        if (failures.Count == 0)
        {
            return "Configuration deserialization failed with no specific failures recorded.";
        }

        if (failures.Count == 1)
        {
            var f = failures[0];
            return $"Configuration deserialization failed for {f.ConfigType.Name}: {f.Message}";
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Configuration deserialization failed for {failures.Count} types:");
        foreach (var failure in failures)
        {
            sb.AppendLine();
            sb.Append(CultureInfo.InvariantCulture, $"  - {failure.ConfigType.Name}: {failure.Message}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Builds a <see cref="ConfigSnapshot"/> by eagerly deserializing all configuration types.
/// Collects failures and supports both fail-fast (startup) and resilient (runtime) modes.
/// </summary>
internal sealed class ConfigSnapshotBuilder
{
    private readonly Dictionary<Type, object> _instances = new();
    private readonly List<DeserializationFailure> _failures = new();
    private readonly ExposureRegistry _bindingRegistry;
    private readonly ConfigManagerCapabilityScope _capabilityScope;

    public ConfigSnapshotBuilder(ExposureRegistry bindingRegistry, ConfigManagerCapabilityScope capabilityScope)
    {
        _bindingRegistry = bindingRegistry;
        _capabilityScope = capabilityScope;
    }

    /// <summary>
    /// Deserializes a configuration type from the merged JSON object.
    /// Failures are collected rather than thrown immediately.
    /// </summary>
    /// <param name="type">The configuration type to deserialize.</param>
    /// <param name="json">The merged JSON object for this type.</param>
    public void DeserializeType(Type type, MutableJsonObject json)
    {
        byte[] bytes;
        lock (json)
        {
            bytes = MutableJsonDocument.ToUtf8Bytes(json);
        }

        string? jsonPreview = null;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var jsonElement = doc.RootElement.Clone();

            jsonPreview = CreateJsonPreview(jsonElement);

            var instance = ConfigurationDeserializer.Deserialize(
                jsonElement,
                type,
                _bindingRegistry.DeserializationMap,
                _capabilityScope);

            if (instance == null)
            {
                _failures.Add(new DeserializationFailure(
                    type,
                    "Deserializer returned null (possible missing required properties)",
                    null,
                    jsonPreview));
                return;
            }

            _instances[type] = instance;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidCastException or NotSupportedException)
        {
            _failures.Add(new DeserializationFailure(
                type,
                ex.Message,
                ex,
                jsonPreview));
        }
    }

    /// <summary>
    /// Indicates whether any deserialization failures occurred.
    /// </summary>
    public bool HasFailures => _failures.Count > 0;

    /// <summary>
    /// Gets the list of deserialization failures.
    /// </summary>
    public IReadOnlyList<DeserializationFailure> Failures => _failures;

    /// <summary>
    /// Builds the snapshot, throwing if any failures occurred.
    /// Use this during startup for fail-fast behavior.
    /// </summary>
    /// <param name="version">The version number for this snapshot.</param>
    /// <returns>A new ConfigSnapshot containing all successfully deserialized instances.</returns>
    /// <exception cref="ConfigurationDeserializationException">Thrown if any deserialization failed.</exception>
    public ConfigSnapshot Build(long version)
    {
        if (_failures.Count > 0)
        {
            throw new ConfigurationDeserializationException(_failures);
        }

        return ConfigSnapshot.Create(_instances, version);
    }

    /// <summary>
    /// Attempts to build the snapshot without throwing.
    /// Use this at runtime for resilient behavior that preserves last-good state.
    /// </summary>
    /// <param name="version">The version number for this snapshot.</param>
    /// <returns>
    /// A tuple containing the snapshot (or null if failures occurred) and the list of failures.
    /// </returns>
    public (ConfigSnapshot? Snapshot, IReadOnlyList<DeserializationFailure> Failures) TryBuild(long version)
    {
        if (_failures.Count > 0)
        {
            return (null, _failures);
        }

        return (ConfigSnapshot.Create(_instances, version), _failures);
    }

    private static string? CreateJsonPreview(JsonElement element)
    {
        // Only include top-level property NAMES, never values.
        // Values could contain plaintext secrets (when AllowPlaintext is enabled)
        // or encrypted envelopes. Neither should persist as strings in memory.
        try
        {
            if (element.ValueKind != JsonValueKind.Object) return "{...}";
            var keys = new List<string>();
            foreach (var prop in element.EnumerateObject())
            {
                keys.Add(prop.Name);
            }
            return keys.Count > 0 ? $"{{ {string.Join(", ", keys)} }}" : "{}";
        }
        catch
        {
            return null;
        }
    }
}
