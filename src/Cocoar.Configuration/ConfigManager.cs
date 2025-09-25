using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Utilities;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Health;
using System.Reactive.Subjects;
using System.Reactive.Linq;

namespace Cocoar.Configuration;

/// <summary>
/// Manages configuration retrieval and orchestrates rule-based configuration processing.
/// Provides health monitoring for production applications to track configuration failures.
/// Implements two-phase error handling: fail-fast during initialization, graceful degradation during runtime.
/// </summary>
public class ConfigManager : IConfigurationAccessor, IDisposable
{
    private readonly List<ConfigRule> _rules;
    private readonly List<BindingSpec> _bindings;
    private readonly ConfigurationRepository _repository;
    private readonly ConfigurationOrchestrator _orchestrator;
    private readonly ChangeSubscriptionManager _subscriptionManager;
    private readonly ReactiveConfigManager _reactiveConfigManager;
    private readonly List<RuleManager> _ruleManagers = new();
    private readonly ProviderRegistry _providerRegistry;
    private readonly BindingRegistry _bindingRegistry;
    private readonly ConfigurationHealthService _healthService;
    private long _healthSequence;
    private long _configVersion; // increments only on successful full recompute
    private readonly ILogger _logger;
    private volatile bool _initialized;
    private readonly object _recomputeGate = new();
    private CancellationTokenSource? _recomputeCts;
    private Task? _currentRecomputeTask;
    private readonly int _debounceMs;
    public ConfigManager(IEnumerable<ConfigRule> rules, IEnumerable<BindingSpec>? bindings = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        _rules = rules.ToList();
        _bindings = bindings?.ToList() ?? new List<BindingSpec>();
        _logger = logger ?? NullLogger.Instance;
        _providerRegistry = new ProviderRegistry(_logger, enableDiagnostics: false, factory: providerFactory);
        _repository = new ConfigurationRepository();
        _orchestrator = new ConfigurationOrchestrator(_repository, _logger);
        _subscriptionManager = new ChangeSubscriptionManager(_logger);
        _bindingRegistry = new BindingRegistry(_bindings, _logger);
        _reactiveConfigManager = new ReactiveConfigManager(_logger, _bindingRegistry);
        
        // Initialize lean health entries (all Unknown)
        var initialEntries = _rules.Select((r,i) => new RuleHealthEntry(
            index: i,
            name: r.Options?.MountPath == null ? null : null, // Name integration to come (placeholder)
            required: r.Options?.Required == true,
            status: RuleResultStatus.Unknown,
            lastSuccessUtc: null,
            lastFailureUtc: null,
            failureCount: 0,
            errorCode: null,
            errorMessage: null)).ToList();
        var initialSnapshot = new ConfigHealthSnapshot(
            id: ++_healthSequence,
            timestampUtc: DateTime.UtcNow,
            configVersion: _configVersion,
            rules: initialEntries);
        _healthService = new ConfigurationHealthService(initialSnapshot);
        _debounceMs = debounceMilliseconds;
    }

    // --- Public API -------------------------------------------------

    /// <summary>
    /// Gets the configuration rules that define where configurations come from.
    /// </summary>
    public IReadOnlyList<ConfigRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// Gets the binding specifications that define interface mappings.
    /// </summary>
    public IReadOnlyList<BindingSpec> Bindings => _bindings.AsReadOnly();

    public ConfigManager Initialize()
    {
        if (_initialized) return this;
        if (Interlocked.CompareExchange(ref _initialized, true, false) == false)
        {
            // Analyze configuration for potential issues
            ConfigurationAnalyzer.AnalyzeDependencies(_rules, _logger);

            // Create per-rule managers
            _ruleManagers.Clear();
            foreach (var r in _rules)
                _ruleManagers.Add(new RuleManager(r, _logger, _providerRegistry));

            // Initial compute and subscriptions - this can throw for required rules (fail-fast)
            try
            {
                _orchestrator.RecomputeAllConfigurationsSafe(_ruleManagers, this);
                RebuildSubscriptions();

                // Report successful initialization for all rules
                ReportSuccessfulRecompute(0);
            }
            catch (Exception ex)
            {
                // During initialization, we still fail-fast for required rules
                _logger.LogError(ex, "ConfigManager initialization failed");
                
                // Report the failed initialization - for simplicity, mark first rule as failed
                ReportFailedRecompute(0, ex);
                throw; // Re-throw to maintain fail-fast behavior during initialization
            }
        }

        return this;
    }

    public T? GetConfig<T>()
    {
        // Try direct lookup first (existing behavior for concrete types)
        if (_repository.TryGetConfiguration<T>(out var jsonElement))
        {
            var registration = _repository.FindRegistration<T>();
            if (registration != null)
            {
                // Deserialize to concrete type, then cast to requested type
                var concreteValue = ConfigurationDeserializer.Deserialize(jsonElement, registration);
                return (T?)concreteValue;
            }
        }

        // Fallback: check binding registry for interface mapping
        var requestedType = typeof(T);
        if (_bindingRegistry.TryGetConcreteType(requestedType, out var concreteType))
        {
            // Found a mapping - try to get config for the concrete type
            if (_repository.TryGetConfiguration(concreteType, out var concreteJsonElement))
            {
                var concreteRegistration = _repository.FindRegistration(concreteType);
                if (concreteRegistration != null)
                {
                    // Deserialize to concrete type, then cast to interface
                    var concreteValue = ConfigurationDeserializer.Deserialize(concreteJsonElement, concreteRegistration);
                    return (T?)concreteValue;
                }
            }
        }

        return default;
    }

    public bool TryGetConfig<T>(out T? value)
    {
        value = GetConfig<T>();
        return value is not null;
    }

    public T GetRequiredConfig<T>()
    {
        var result = GetConfig<T>();
        if (result is null)
        {
            throw new InvalidOperationException($"Configuration for type {typeof(T).Name} not found.");
        }

        return result;
    }

    public object? GetConfig(Type type)
    {
        // Try direct lookup first
        if (_repository.TryGetConfiguration(type, out var jsonElement))
        {
            var registration = _repository.FindRegistration(type);
            if (registration != null)
            {
                return ConfigurationDeserializer.Deserialize(jsonElement, registration);
            }
        }

        // Fallback: check binding registry for interface mapping
        if (_bindingRegistry.TryGetConcreteType(type, out var concreteType))
        {
            // Found a mapping - try to get config for the concrete type
            if (_repository.TryGetConfiguration(concreteType, out var concreteJsonElement))
            {
                var concreteRegistration = _repository.FindRegistration(concreteType);
                if (concreteRegistration != null)
                {
                    return ConfigurationDeserializer.Deserialize(concreteJsonElement, concreteRegistration);
                }
            }
        }

        return null;
    }

    public bool TryGetConfig(Type type, out object? value)
    {
        value = GetConfig(type);
        return value is not null;
    }

    public object GetRequiredConfig(Type type)
    {
        var value = GetConfig(type);
        return value ?? throw new InvalidOperationException($"Configuration for type {type.Name} not found.");
    }

    public JsonElement? GetConfigAsJson(Type type)
    {
        return _repository.GetConfigurationAsJson(type);
    }

    // --- Reactive Configuration --------------------------------------

    /// <summary>
    /// Gets or creates a reactive configuration that provides both observable updates and current value access.
    /// Perfect for dependency injection scenarios where you need both reactive updates and immediate value access.
    /// The returned observable is error-resilient and will never terminate due to subscriber errors.
    /// </summary>
    /// <typeparam name="T">The configuration type to observe</typeparam>
    /// <returns>A reactive configuration that emits configuration values and provides current value access</returns>
    public IReactiveConfig<T> GetReactiveConfig<T>()
    {
        var t = typeof(T);
        if (IsValueTupleType(t))
        {
            // Build tuple reactive config dynamically
            return (IReactiveConfig<T>)CreateTupleReactiveConfig(t);
        }
        return _reactiveConfigManager.GetReactiveConfig(() => GetConfig<T>()!);
    }

    // --- Lifecycle --------------------------------------------------

    public void Dispose()
    {
        _subscriptionManager.Dispose();
        _reactiveConfigManager.Dispose();
    _healthService.Dispose();
        
        foreach (var rm in _ruleManagers.ToArray())
        {
            try
            {
                rm.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }

        try { _recomputeCts?.Cancel(); } catch { /* ignore */ }
        _recomputeCts?.Dispose();

        _ruleManagers.Clear();
        _initialized = false;
        GC.SuppressFinalize(this);
    }

    // --- Private helpers (implementation details kept at the end) --

    private void RebuildSubscriptions()
    {
        _subscriptionManager.CreateSubscriptions(_ruleManagers,
            startIndex => ScheduleRecompute(startIndex), _debounceMs, 40);
    }

    private void ScheduleRecompute(int startIndex)
    {
        CancellationTokenSource cts;
        Task task;
        lock (_recomputeGate)
        {
            try { _recomputeCts?.Cancel(); } catch { /* ignore */ }
            _recomputeCts?.Dispose();
            _recomputeCts = new CancellationTokenSource();
            cts = _recomputeCts;
            task = _currentRecomputeTask = Task.Run(() =>
            {
                try
                {
                    // Runtime recompute - preserve current config on failure
                    _orchestrator.RecomputeAllConfigurationsSafe(_ruleManagers, this, startIndex, cts.Token);
                    
                    // Report successful recompute for affected rules
                    ReportSuccessfulRecompute(startIndex);
                    
                    // Notify reactive config observers of the updated configurations
                    _reactiveConfigManager.NotifyConfigurationObservers(type => GetConfig(type));
                }
                catch (OperationCanceledException)
                {
                    // Swallow expected cancellation
                }
                catch (Exception ex)
                {
                    // RUNTIME ERROR HANDLING: This is your key requirement!
                    // - Preserve current configuration (don't change repository)
                    // - Update health status to reflect the failure
                    // - Log the error but don't crash the application
                    
                    _logger.LogError(ex, "Runtime recompute failed - preserving current configuration");
                    
                    // Report failure for affected rules using simple approach
                    ReportFailedRecompute(startIndex, ex);
                    
                    // Note: We deliberately do NOT re-throw the exception here
                    // This implements the "preserve current config" behavior
                }
            }, cts.Token);
        }
    }

    // Exposed for testing (optional) to await current recompute
    internal Task? CurrentRecomputeTask => _currentRecomputeTask;

    // --- Simple Health Tracking ---
    
    private void PublishSnapshot(List<RuleHealthEntry> entries, bool incrementVersion)
    {
        if (incrementVersion) _configVersion++;
        var snapshot = new ConfigHealthSnapshot(++_healthSequence, DateTime.UtcNow, _configVersion, entries);
        _healthService.Publish(snapshot);
    }

    private List<RuleHealthEntry> CloneCurrentEntries()
        => _healthService.Snapshot.Rules.Select(r => new RuleHealthEntry(r.Index, r.Name, r.Required, r.Status, r.LastSuccessUtc, r.LastFailureUtc, r.FailureCount, r.ErrorCode, r.ErrorMessage)).ToList();

    private void ReportSuccessfulRecompute(int startIndex)
    {
        var list = BuildEntriesFromOutcomes();
        PublishSnapshot(list, incrementVersion: true);
    }

    private void ReportFailedRecompute(int startIndex, Exception exception)
    {
        // Build from outcomes then mark trailing entries after first required failure as Unknown if they were not re-evaluated
        var list = BuildEntriesFromOutcomes(forceTrailingUnknown:true);
        PublishSnapshot(list, incrementVersion: false);
    }

    private List<RuleHealthEntry> BuildEntriesFromOutcomes(bool forceTrailingUnknown = false)
    {
        var now = DateTime.UtcNow;
        var current = _healthService.Snapshot.Rules.ToDictionary(r => r.Index, r => r);
        // Pre-populate list with prior entries (or synthetic Unknown) so we can index-replace safely
        var list = new List<RuleHealthEntry>(_ruleManagers.Count);
        for (int seed = 0; seed < _ruleManagers.Count; seed++)
        {
            if (current.TryGetValue(seed, out var existing))
                list.Add(existing);
            else
                list.Add(new RuleHealthEntry(seed, null, _ruleManagers[seed].Required, RuleResultStatus.Unknown, null, null, 0, null, null));
        }
        bool encounteredRequiredFailure = false;
        for (int i = 0; i < _ruleManagers.Count; i++)
        {
            var rm = _ruleManagers[i];
            var prev = list[i];
            RuleHealthEntry updated = prev;
            switch (rm.LastOutcome)
            {
                case RuleManager.RuleExecutionOutcome.Unknown:
                    updated = prev; break; // untouched
                case RuleManager.RuleExecutionOutcome.Up:
                    if (prev.Status != RuleResultStatus.Up)
                        updated = prev.WithStatus(RuleResultStatus.Up, now);
                    else
                        updated = prev; // keep last success timestamp
                    break;
                case RuleManager.RuleExecutionOutcome.Skipped:
                    if (prev.Status != RuleResultStatus.Skipped)
                        updated = prev.WithStatus(RuleResultStatus.Skipped, now);
                    break;
                case RuleManager.RuleExecutionOutcome.Failed:
                    var ex = rm.LastFailureException ?? new Exception("FAILED");
                    updated = prev.WithStatus(RuleResultStatus.Down, now, MapException(ex), ShortMessage(ex));
                    if (rm.Required) encounteredRequiredFailure = true;
                    break;
            }
            list[i] = updated;
            if (forceTrailingUnknown && encounteredRequiredFailure && i < _ruleManagers.Count - 1)
            {
                // For remaining rules that appear after required failure, set to Unknown (unless already Down)
                for (int j = i + 1; j < _ruleManagers.Count; j++)
                {
                    var existing = list[j];
                    if (existing.Status is RuleResultStatus.Up or RuleResultStatus.Skipped)
                        list[j] = new RuleHealthEntry(existing.Index, existing.Name, existing.Required, RuleResultStatus.Unknown, existing.LastSuccessUtc, existing.LastFailureUtc, existing.FailureCount, existing.ErrorCode, existing.ErrorMessage);
                }
                break; // after first required failure we exit loop (later outcomes not valid this pass)
            }
        }
        return list;
    }

    private static string MapException(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("timeout")) return HealthErrorCodes.HttpTimeout;
        if (msg.Contains("404") || msg.Contains("not found")) return HealthErrorCodes.FileNotFound;
        if (msg.Contains("json")) return HealthErrorCodes.JsonParse;
        return HealthErrorCodes.HttpErrorStatus; // generic fallback
    }
    private static string ShortMessage(Exception ex) => ex.Message.Length > 200 ? ex.Message.Substring(0, 200) : ex.Message;

    // --- Public Health API ---

    /// <summary>
    /// Gets the current health information.
    /// </summary>
    public IConfigurationHealthService GetHealthService() => _healthService;

    private static bool IsValueTupleType(Type t) => t.IsValueType && t.FullName != null && t.FullName.StartsWith("System.ValueTuple");

    private object CreateTupleReactiveConfig(Type tupleType)
    {
        // Determine element types (flattened) and validate each is a configured concrete type or bound interface
        var elementTypes = FlattenTuple(tupleType).ToArray();
        if (elementTypes.Length == 0)
            throw new InvalidOperationException($"Type {tupleType.Name} is not a non-empty ValueTuple");

        var allowedConcrete = new HashSet<Type>(_rules.Select(r => r.ConcreteType));
        var allowedInterfaces = new HashSet<Type>(_bindings.SelectMany(b => b.BoundInterfaces));
        var invalid = new List<string>();
        foreach (var et in elementTypes)
        {
            if (et.IsInterface)
            {
                if (!allowedInterfaces.Contains(et)) invalid.Add(et.Name + " (interface not bound)");
            }
            else
            {
                if (!allowedConcrete.Contains(et)) invalid.Add(et.Name + " (not a configured type)");
            }
        }
        if (invalid.Count > 0)
            throw new InvalidOperationException($"Cannot create IReactiveConfig<{tupleType.Name}>. The following tuple element types are not configured/bound: {string.Join(", ", invalid)}");

        // Prime all element types so ReactiveConfigManager tracks them and emits per-pass events
        foreach (var et in elementTypes.Distinct())
        {
            try
            {
                var m = GetType().GetMethod("GetReactiveConfig")!.MakeGenericMethod(et);
                _ = m.Invoke(this, null);
            }
            catch { /* non-fatal */ }
        }
        var generic = typeof(ReactiveTupleConfig<>).MakeGenericType(tupleType);
        // constructor: (ConfigManager, ReactiveConfigManager, ILogger)
        var instance = Activator.CreateInstance(generic, this, _reactiveConfigManager, _logger)!;
        return instance;
    }

    private static IEnumerable<Type> FlattenTuple(Type t)
    {
        if (!(t.IsValueType && t.FullName != null && t.FullName.StartsWith("System.ValueTuple"))) yield break;
        var fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (f.Name == "Rest" && f.FieldType.FullName != null && f.FieldType.FullName.StartsWith("System.ValueTuple"))
            {
                foreach (var inner in FlattenTuple(f.FieldType)) yield return inner;
            }
            else yield return f.FieldType;
        }
    }
}
