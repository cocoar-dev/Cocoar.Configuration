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
/// Coordinates JSON storage, backplane publication, and health tracking.
/// Uses MasterBackplane for atomic, eager-deserialized configuration updates.
/// </summary>
internal class ConfigurationState : IDisposable
{
    private readonly ConfigJsonRepository _jsonRepository = new();
    private readonly ConfigurationHealthReporter _healthReporter;
    private readonly ILogger _logger;
    private long _configVersion;

    // Master Backplane fields
    private MasterBackplane? _backplane;
    private bool _isStartupPhase = true;
    private IReadOnlyList<DeserializationFailure> _lastDeserializationFailures = [];

    public ConfigurationState(List<RuleManager> ruleManagers, List<ConfigRule> rules, ILogger logger, IFlagsHealthSource? flagsHealthSource = null)
    {
        _logger = logger;
        _healthReporter = new ConfigurationHealthReporter(ruleManagers, rules, flagsHealthSource);
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
    /// Gets the current configuration dictionary (or pending if available).
    /// Thread-safe access to avoid race conditions.
    /// </summary>
    public Dictionary<Type, MutableJsonObject> CurrentConfigurations => _jsonRepository.CurrentConfigurations;

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

    public void BeginUpdate() => _jsonRepository.BeginUpdate();

    public void UpdateConfiguration(Type type, MutableJsonObject value) => _jsonRepository.UpdateConfiguration(type, value);

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
            _jsonRepository.CommitUpdate(finalConfigurations);
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
                _jsonRepository.CommitUpdate(finalConfigurations);
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
                _jsonRepository.RollbackUpdate();
            }
        }
    }

    /// <summary>
    /// Legacy commit that only stores JSON without deserialization.
    /// Used during the transition period.
    /// </summary>
    public void CommitUpdate(Dictionary<Type, MutableJsonObject> finalConfigurations)
        => _jsonRepository.CommitUpdate(finalConfigurations);

    public void RollbackUpdate() => _jsonRepository.RollbackUpdate();

    public Type? FindRegistration<T>() => _jsonRepository.FindRegistration<T>();

    public Type? FindRegistration(Type type) => _jsonRepository.FindRegistration(type);

    public bool TryGetConfiguration<T>(out MutableJsonObject? value) => _jsonRepository.TryGetConfiguration<T>(out value);

    public bool TryGetConfiguration(Type type, out MutableJsonObject? value) => _jsonRepository.TryGetConfiguration(type, out value);

    public JsonElement? GetConfigurationAsJson(Type type) => _jsonRepository.GetConfigurationAsJson(type);

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
    }
}
