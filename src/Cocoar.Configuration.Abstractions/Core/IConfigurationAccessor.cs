using System.Text.Json;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Provides access to configuration snapshots.
/// Used by rule factories to reference earlier configuration state when building dependent rules.
/// </summary>
public interface IConfigurationAccessor
{
    T? GetConfig<T>();
    bool TryGetConfig<T>(out T? value);

    /// <summary>
    /// Gets configuration, throwing if not found.
    /// Use this in rule factories when a dependent configuration must exist before the rule can execute.
    /// </summary>
    T GetRequiredConfig<T>();
    object? GetConfig(Type type);
    bool TryGetConfig(Type type, out object? value);
    object GetRequiredConfig(Type type);
    JsonElement? GetConfigAsJson(Type type);
}
