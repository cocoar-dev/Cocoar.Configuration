using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Utilities;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration;

/// <summary>
/// Manages configuration retrieval and orchestrates rule-based configuration processing.
/// Refactored to use focused helper classes for better maintainability.
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

            // Initial compute and subscriptions
            _orchestrator.RecomputeAllConfigurationsSafe(_ruleManagers, this);
            RebuildSubscriptions();
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
        return _reactiveConfigManager.GetReactiveConfig(() => GetConfig<T>()!);
    }

    // --- Lifecycle --------------------------------------------------

    public void Dispose()
    {
        _subscriptionManager.Dispose();
        _reactiveConfigManager.Dispose();
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
                    _orchestrator.RecomputeAllConfigurationsSafe(_ruleManagers, this, startIndex, cts.Token);
                    
                    // Notify reactive config observers of the updated configurations
                    _reactiveConfigManager.NotifyConfigurationObservers(type => GetConfig(type));
                }
                catch (OperationCanceledException)
                {
                    // Swallow expected cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recompute failed");
                }
            }, cts.Token);
        }
    }

    // Exposed for testing (optional) to await current recompute
    internal Task? CurrentRecomputeTask => _currentRecomputeTask;
}
