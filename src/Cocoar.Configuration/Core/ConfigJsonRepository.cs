using System.Text.Json;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Pure storage for configuration JSON data.
/// Manages committed and pending configurations with transaction-like semantics.
/// </summary>
internal sealed class ConfigJsonRepository
{
    private volatile Dictionary<Type, MutableJsonObject> _configs = new();
    private volatile Dictionary<Type, MutableJsonObject>? _pendingConfigurations;

    /// <summary>
    /// Gets the current configuration dictionary (pending if in transaction, otherwise committed).
    /// Thread-safe via volatile field access.
    /// </summary>
    public Dictionary<Type, MutableJsonObject> CurrentConfigurations
    {
        get
        {
            // Capture volatile field once to avoid torn reads
            var pending = _pendingConfigurations;
            return pending ?? _configs;
        }
    }

    /// <summary>
    /// Gets the committed configurations (ignores any pending transaction).
    /// </summary>
    public Dictionary<Type, MutableJsonObject> CommittedConfigurations => _configs;

    /// <summary>
    /// Indicates whether a transaction is in progress.
    /// </summary>
    public bool HasPendingTransaction => _pendingConfigurations != null;

    /// <summary>
    /// Begins a new update transaction.
    /// </summary>
    public void BeginUpdate()
    {
        _pendingConfigurations = new();
    }

    /// <summary>
    /// Updates a configuration within the current transaction.
    /// </summary>
    public void UpdateConfiguration(Type type, MutableJsonObject value)
    {
        if (_pendingConfigurations == null)
        {
            throw new InvalidOperationException("Must call BeginUpdate() before updating configurations");
        }

        _pendingConfigurations[type] = value;
    }

    /// <summary>
    /// Commits the transaction with the given final configurations.
    /// </summary>
    public void CommitUpdate(Dictionary<Type, MutableJsonObject> finalConfigurations)
    {
        _configs = finalConfigurations;
        _pendingConfigurations = null;
    }

    /// <summary>
    /// Rolls back the current transaction, discarding pending changes.
    /// </summary>
    public void RollbackUpdate()
    {
        _pendingConfigurations = null;
    }

    /// <summary>
    /// Finds a configuration registration by type.
    /// </summary>
    public Type? FindRegistration<T>() => FindRegistration(typeof(T));

    /// <summary>
    /// Finds a configuration registration by type.
    /// Thread-safe to avoid race conditions.
    /// </summary>
    public Type? FindRegistration(Type type)
    {
        var currentConfigs = CurrentConfigurations;
        return currentConfigs.ContainsKey(type) ? type : null;
    }

    /// <summary>
    /// Tries to get a configuration value by type.
    /// </summary>
    public bool TryGetConfiguration<T>(out MutableJsonObject? value) => TryGetConfiguration(typeof(T), out value);

    /// <summary>
    /// Tries to get a configuration value by type.
    /// Thread-safe to avoid race conditions with volatile fields.
    /// </summary>
    public bool TryGetConfiguration(Type type, out MutableJsonObject? value)
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

    /// <summary>
    /// Gets a configuration as a JsonElement.
    /// </summary>
    public JsonElement? GetConfigurationAsJson(Type type)
    {
        var currentConfigs = CurrentConfigurations;
        if (currentConfigs.TryGetValue(type, out var value))
        {
            byte[] bytes;
            lock (value)
            {
                bytes = MutableJsonDocument.ToUtf8Bytes(value);
            }
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }
        return null;
    }
}
