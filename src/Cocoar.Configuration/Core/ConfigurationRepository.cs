using System.Text.Json;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Repository for storing and retrieving configuration data by type.
/// Handles the storage logic and type resolution for configurations.
/// </summary>
internal class ConfigurationRepository
{
    private volatile Dictionary<Type, JsonElement> _configs = new();
    private volatile Dictionary<Type, JsonElement>? _pendingConfigurations;

    /// <summary>
    /// Gets the current configuration dictionary (or pending if available).
    /// Thread-safe access to avoid race conditions.
    /// </summary>
    public Dictionary<Type, JsonElement> CurrentConfigurations
    {
        get
        {
            // Capture volatile field once to avoid race condition
            var pending = _pendingConfigurations;
            return pending ?? _configs;
        }
    }

    public void BeginUpdate()
    {
        _pendingConfigurations = new();
    }

    public void UpdateConfiguration(Type type, JsonElement value)
    {
        if (_pendingConfigurations == null)
        {
            throw new InvalidOperationException("Must call BeginUpdate() before updating configurations");
        }

        _pendingConfigurations[type] = value;
    }

    public void CommitUpdate(Dictionary<Type, JsonElement> finalConfigurations)
    {
        _configs = finalConfigurations;
        _pendingConfigurations = null;
    }

    public void RollbackUpdate()
    {
        _pendingConfigurations = null;
    }

    public Type? FindRegistration<T>() => FindRegistration(typeof(T));

    /// <summary>
    /// Finds a configuration registration by concrete type.
    /// Thread-safe to avoid race conditions.
    /// </summary>
    public Type? FindRegistration(Type type)
    {
        var currentConfigs = CurrentConfigurations;
        return currentConfigs.ContainsKey(type) ? type : null;
    }

    public bool TryGetConfiguration<T>(out JsonElement value)
    {
        return TryGetConfiguration(typeof(T), out value);
    }

    /// <summary>
    /// Tries to get a configuration value by type.
    /// Thread-safe to avoid race conditions with volatile fields.
    /// </summary>
    public bool TryGetConfiguration(Type type, out JsonElement value)
    {
        var foundType = FindRegistration(type);
        if (foundType != null)
        {
            var currentConfigs = CurrentConfigurations;
            if (currentConfigs.TryGetValue(foundType, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    public JsonElement? GetConfigurationAsJson(Type type)
    {
        var currentConfigs = CurrentConfigurations;
        return currentConfigs.TryGetValue(type, out var value) ? value.Clone() : null;
    }
}
