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
    private readonly List<RuleManager> _ruleManagers;
    private readonly ConfigurationHealthService _healthService;
    private readonly ILogger _logger;
    private long _healthSequence;
    private long _configVersion;

    // Master Backplane fields
    private MasterBackplane? _backplane;
    private bool _isStartupPhase = true;
    private IReadOnlyList<DeserializationFailure> _lastDeserializationFailures = [];

    public ConfigurationState(List<RuleManager> ruleManagers, List<ConfigRule> rules, ILogger logger)
    {
        _ruleManagers = ruleManagers;
        _logger = logger;
        var initialEntries = rules.Select((r, i) => new RuleHealthEntry(
            index: i,
            name: r.Options?.Name,
            required: r.Options?.Required == true,
            status: RuleResultStatus.Unknown,
            lastSuccessUtc: null,
            lastFailureUtc: null,
            failureCount: 0,
            errorCode: null,
            errorMessage: null,
            providerType: r.ProviderType.Name,
            configType: r.ConcreteType.Name,
            deserializationStatus: null)).ToList();

        var initialSnapshot = new ConfigHealthSnapshot(
            id: ++_healthSequence,
            timestampUtc: DateTime.UtcNow,
            configVersion: _configVersion,
            rules: initialEntries);

        _healthService = new(initialSnapshot);
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

    public IConfigurationHealthService GetHealthService() => _healthService;

    public void ReportSuccessfulRecompute(int startIndex)
    {
        var list = BuildEntriesFromOutcomes();
        // Don't increment version here - it's already incremented in CommitUpdateWithDeserialization
        PublishHealthSnapshot(list, incrementVersion: false);
    }

    public void ReportFailedRecompute(int startIndex, Exception exception)
    {
        var list = BuildEntriesFromOutcomes(forceTrailingUnknown: true);
        PublishHealthSnapshot(list, incrementVersion: false);
    }

    /// <summary>
    /// Reports deserialization failures to the health service.
    /// Call this after a runtime deserialization failure to update health status.
    /// </summary>
    public void ReportDeserializationFailures(IReadOnlyList<DeserializationFailure> failures)
    {
        if (failures.Count == 0) return;

        var list = BuildEntriesFromOutcomes();

        // Add deserialization status to affected rules
        var failuresByType = failures.ToDictionary(f => f.ConfigType.Name, f => f);

        for (var i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (entry.ConfigType != null && failuresByType.TryGetValue(entry.ConfigType, out var failure))
            {
                list[i] = entry.WithDeserializationFailure(failure.Message);
            }
        }

        PublishHealthSnapshot(list, incrementVersion: false);
    }

    private void PublishHealthSnapshot(List<RuleHealthEntry> entries, bool incrementVersion)
    {
        if (incrementVersion)
        {
            _configVersion++;
        }

        var snapshot = new ConfigHealthSnapshot(++_healthSequence, DateTime.UtcNow, _configVersion, entries);
        _healthService.Publish(snapshot);
    }

    private List<RuleHealthEntry> BuildEntriesFromOutcomes(bool forceTrailingUnknown = false)
    {
        var now = DateTime.UtcNow;
        var current = _healthService.Snapshot.Rules.ToDictionary(r => r.Index, r => r);
        var list = new List<RuleHealthEntry>(_ruleManagers.Count);

        for (var seed = 0; seed < _ruleManagers.Count; seed++)
        {
            if (current.TryGetValue(seed, out var existing))
            {
                list.Add(existing);
            }
            else
            {
                list.Add(new(seed, null, _ruleManagers[seed].Required, RuleResultStatus.Unknown, null, null, 0, null, null, null, null, null));
            }
        }

        var encounteredRequiredFailure = false;
        for (var i = 0; i < _ruleManagers.Count; i++)
        {
            var rm = _ruleManagers[i];
            var prev = list[i];
            RuleHealthEntry updated = prev;

            switch (rm.LastOutcome)
            {
                case RuleManager.RuleExecutionOutcome.Unknown:
                    updated = prev;
                    break;
                case RuleManager.RuleExecutionOutcome.Up:
                    updated = prev.Status != RuleResultStatus.Up ? prev.WithStatus(RuleResultStatus.Up, now) : prev;
                    break;
                case RuleManager.RuleExecutionOutcome.Skipped:
                    if (prev.Status != RuleResultStatus.Skipped)
                    {
                        updated = prev.WithStatus(RuleResultStatus.Skipped, now);
                    }
                    break;
                case RuleManager.RuleExecutionOutcome.Failed:
                    var ex = rm.LastFailureException ?? new InvalidOperationException("Rule failed without exception details");
                    updated = prev.WithStatus(RuleResultStatus.Down, now, MapException(ex), ShortMessage(ex));
                    if (rm.Required)
                    {
                        encounteredRequiredFailure = true;
                    }
                    break;
            }

            list[i] = updated;
            if (forceTrailingUnknown && encounteredRequiredFailure && i < _ruleManagers.Count - 1)
            {
                for (var j = i + 1; j < _ruleManagers.Count; j++)
                {
                    var existing = list[j];
                    if (existing.Status is RuleResultStatus.Up or RuleResultStatus.Skipped)
                    {
                        list[j] = new(existing.Index, existing.Name, existing.Required, RuleResultStatus.Unknown, existing.LastSuccessUtc, existing.LastFailureUtc, existing.FailureCount, existing.ErrorCode, existing.ErrorMessage, existing.ProviderType, existing.ConfigType, existing.DeserializationStatus);
                    }
                }
                break;
            }
        }
        return list;
    }

    private static string? MapException(Exception ex)
    {
        static bool TryGetCodeFromData(Exception e, out string? code)
        {
            code = null;
            if (e.Data is { Count: > 0 })
            {
                if (e.Data.Contains("HealthErrorCode") && e.Data["HealthErrorCode"] is string c1 && !string.IsNullOrWhiteSpace(c1))
                {
                    code = c1; return true;
                }
                if (e.Data.Contains("ErrorCode") && e.Data["ErrorCode"] is string c2 && !string.IsNullOrWhiteSpace(c2))
                {
                    code = c2; return true;
                }
            }
            return false;
        }

        if (TryGetCodeFromData(ex, out var codeFromEx))
        {
            return codeFromEx;
        }

        if (ex is AggregateException { InnerException: { } inner } && TryGetCodeFromData(inner, out var codeFromInner))
        {
            return codeFromInner;
        }

        return null;
    }

    private static string ShortMessage(Exception ex) => ex.Message.Length > 200 ? ex.Message.Substring(0, 200) : ex.Message;

    public void Dispose()
    {
        _healthService.Dispose();
        _backplane?.Dispose();
        GC.SuppressFinalize(this);
    }
}
