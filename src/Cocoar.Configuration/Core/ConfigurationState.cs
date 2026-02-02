using System.Text.Json;
using Cocoar.Capabilities;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Rules;
using Cocoar.Json.Mutable;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Core;

internal static partial class ConfigurationStateLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Error,
        Message = "Deserialization failed for {TypeName}: {Message}")]
    public static partial void DeserializationFailed(this ILogger logger, Exception? ex, string typeName, string message);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Runtime deserialization failed for {FailureCount} types, keeping last good configuration")]
    public static partial void RuntimeDeserializationFailed(this ILogger logger, int failureCount);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information,
        Message = "Configuration snapshot published: version={Version}, types={TypeCount}")]
    public static partial void SnapshotPublished(this ILogger logger, long version, int typeCount);
}

/// <summary>
/// Central state management for configurations and health monitoring.
/// Combines configuration storage (repository) with health tracking.
/// Uses MasterBackplane for atomic, eager-deserialized configuration updates.
/// </summary>
internal class ConfigurationState : IDisposable
{
    private volatile Dictionary<Type, MutableJsonObject> _configs = new();
    private volatile Dictionary<Type, MutableJsonObject>? _pendingConfigurations;
    private readonly ConfigurationHealthReporter _healthReporter;
    private readonly ILogger _logger;
    private long _configVersion;

    // Master Backplane fields
    private MasterBackplane? _backplane;
    private bool _isStartupPhase = true;
    private IReadOnlyList<DeserializationFailure> _lastDeserializationFailures = [];

    public ConfigurationState(List<RuleManager> ruleManagers, List<ConfigRule> rules, ILogger logger)
    {
        _logger = logger;
        _healthReporter = new ConfigurationHealthReporter(ruleManagers, rules);
    }

    /// <summary>
    /// Gets the MasterBackplane for accessing cached configuration instances.
    /// </summary>
    public MasterBackplane Backplane => _backplane ?? throw new InvalidOperationException("Backplane not initialized. Call InitializeBackplane first.");

    /// <summary>
    /// Indicates whether the system is still in the startup phase.
    /// During startup, deserialization failures throw exceptions.
    /// After startup, failures preserve the last good configuration.
    /// </summary>
    public bool IsStartupPhase => _isStartupPhase;

    /// <summary>
    /// Gets the last deserialization failures that occurred during runtime (non-startup) updates.
    /// </summary>
    public IReadOnlyList<DeserializationFailure> LastDeserializationFailures => _lastDeserializationFailures;

    /// <summary>
    /// Initializes the MasterBackplane with the binding registry.
    /// Must be called before CommitUpdateWithDeserialization.
    /// </summary>
    internal void InitializeBackplane(ExposureRegistry bindingRegistry)
    {
        _backplane = new MasterBackplane(bindingRegistry);
    }

    /// <summary>
    /// Marks the startup phase as complete.
    /// After this, deserialization failures will preserve last good configuration instead of throwing.
    /// </summary>
    public void MarkStartupComplete()
    {
        _isStartupPhase = false;
    }

    /// <summary>
    /// Gets the current configuration dictionary (or pending if available).
    /// Thread-safe access to avoid race conditions.
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

    public void BeginUpdate()
    {
        _pendingConfigurations = new();
    }

    public void UpdateConfiguration(Type type, MutableJsonObject value)
    {
        if (_pendingConfigurations == null)
        {
            throw new InvalidOperationException("Must call BeginUpdate() before updating configurations");
        }

        _pendingConfigurations[type] = value;
    }

    /// <summary>
    /// Commits the update with eager deserialization to the MasterBackplane.
    /// During startup, throws on any deserialization failure.
    /// At runtime, keeps last good configuration on failure.
    /// </summary>
    public void CommitUpdateWithDeserialization(
        Dictionary<Type, MutableJsonObject> finalConfigurations,
        ExposureRegistry bindingRegistry,
        ConfigManagerCapabilityScope capabilityScope)
    {
        // Build the snapshot with eager deserialization
        var builder = new ConfigSnapshotBuilder(bindingRegistry, capabilityScope);

        foreach (var (type, json) in finalConfigurations)
        {
            builder.DeserializeType(type, json);
        }

        if (_isStartupPhase)
        {
            // Startup: fail fast with all errors
            var snapshot = builder.Build(++_configVersion);
            _backplane?.Publish(snapshot);
            _logger.SnapshotPublished(snapshot.Version, snapshot.Count);

            // Store the raw JSON for backward compatibility with GetConfigurationAsJson
            _configs = finalConfigurations;
            _pendingConfigurations = null;
        }
        else
        {
            // Runtime: keep last good on failure
            var (snapshot, failures) = builder.TryBuild(++_configVersion);

            if (snapshot != null)
            {
                _lastDeserializationFailures = [];
                _backplane?.Publish(snapshot);
                _logger.SnapshotPublished(snapshot.Version, snapshot.Count);

                // Only update JSON when deserialization succeeds - keeps consistency
                // between GetConfig<T>() (cached instances) and GetConfigAsJson() (raw JSON)
                _configs = finalConfigurations;
                _pendingConfigurations = null;
            }
            else
            {
                _lastDeserializationFailures = failures;
                _logger.RuntimeDeserializationFailed(failures.Count);

                foreach (var failure in failures)
                {
                    _logger.DeserializationFailed(failure.Exception, failure.ConfigType.Name, failure.Message);
                }

                // Rollback: keep old JSON AND old cached instances
                // Don't update _configs - keep the last good JSON
                // Don't decrement version - we want to track that an attempt was made
                _pendingConfigurations = null;
            }
        }
    }

    /// <summary>
    /// Legacy commit that only stores JSON without deserialization.
    /// Used during the transition period.
    /// </summary>
    public void CommitUpdate(Dictionary<Type, MutableJsonObject> finalConfigurations)
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

    public IConfigurationHealthService GetHealthService() => _healthReporter.HealthService;

    public void ReportSuccessfulRecompute(int startIndex)
        => _healthReporter.ReportSuccessfulRecompute(startIndex, _configVersion);

    public void ReportFailedRecompute(int startIndex, Exception exception)
        => _healthReporter.ReportFailedRecompute(startIndex, exception, _configVersion);

    /// <summary>
    /// Reports deserialization failures to the health service.
    /// Call this after a runtime deserialization failure to update health status.
    /// </summary>
    public void ReportDeserializationFailures(IReadOnlyList<DeserializationFailure> failures)
        => _healthReporter.ReportDeserializationFailures(failures, _configVersion);

    public void Dispose()
    {
        _healthReporter.Dispose();
        _backplane?.Dispose();
        GC.SuppressFinalize(this);
    }
}
