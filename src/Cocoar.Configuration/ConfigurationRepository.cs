using System.Text.Json;

namespace Cocoar.Configuration;

/// <summary>
/// Repository for storing and retrieving configuration data by type.
/// Handles the storage logic and type resolution for configurations.
/// </summary>
internal class ConfigurationRepository
{
    private volatile Dictionary<ConfigRegistration, JsonElement> _configs = new();
    private volatile Dictionary<ConfigRegistration, JsonElement>? _pendingConfigurations;

    /// <summary>
    /// Gets the current configuration dictionary (or pending if available).
    /// Thread-safe access to avoid race conditions.
    /// </summary>
    public Dictionary<ConfigRegistration, JsonElement> CurrentConfigurations
    {
        get
        {
            // Capture volatile field once to avoid race condition
            var pending = _pendingConfigurations;
            return pending ?? _configs;
        }
    }

    /// <summary>
    /// Starts a configuration update session, allowing incremental updates.
    /// </summary>
    public void BeginUpdate()
    {
        _pendingConfigurations = new Dictionary<ConfigRegistration, JsonElement>();
    }

    /// <summary>
    /// Adds or updates a configuration during an update session.
    /// </summary>
    public void UpdateConfiguration(ConfigRegistration registration, JsonElement value)
    {
        if (_pendingConfigurations == null)
            throw new InvalidOperationException("Must call BeginUpdate() before updating configurations");

        _pendingConfigurations[registration] = value;
    }

    /// <summary>
    /// Commits all pending changes atomically.
    /// </summary>
    public void CommitUpdate(Dictionary<ConfigRegistration, JsonElement> finalConfigurations)
    {
        _configs = finalConfigurations;
        _pendingConfigurations = null; // Clear working snapshot after atomic swap
    }

    /// <summary>
    /// Abandons any in-progress pending update without committing changes.
    /// </summary>
    public void RollbackUpdate()
    {
        _pendingConfigurations = null;
    }

    /// <summary>
    /// Finds a configuration registration by type (concrete or contract type).
    /// </summary>
    public ConfigRegistration? FindRegistration<T>()
    {
        return FindRegistration(typeof(T));
    }

    /// <summary>
    /// Finds a configuration registration by type (concrete or contract type).
    /// Thread-safe to avoid race conditions.
    /// </summary>
    public ConfigRegistration? FindRegistration(Type type)
    {
        // Capture current configurations once to avoid race condition
        var currentConfigs = CurrentConfigurations;
        return currentConfigs.Keys.FirstOrDefault(k => k.ConcreteType == type)
               ?? currentConfigs.Keys.FirstOrDefault(k => k.ContractType == type);
    }

    /// <summary>
    /// Tries to get a configuration value by type.
    /// </summary>
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
        var registration = FindRegistration(type);
        if (registration != null)
        {
            // Capture current configurations once to avoid race condition
            var currentConfigs = CurrentConfigurations;
            if (currentConfigs.TryGetValue(registration, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets configuration as JSON element for the specified type.
    /// </summary>
    public JsonElement? GetConfigurationAsJson(Type type)
    {
        return _configs.TryGetValue(new ConfigRegistration(type), out var value) ? value.Clone() : null;
    }
}
