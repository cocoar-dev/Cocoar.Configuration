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
    private readonly ConfigurationRepository _repository;
    private readonly ConfigurationOrchestrator _orchestrator;
    private readonly ChangeSubscriptionManager _subscriptionManager;
    private readonly List<RuleManager> _ruleManagers = new();
    private readonly ProviderRegistry _providerRegistry;
    private readonly ILogger _logger;
    private volatile bool _initialized;
    private readonly object _recomputeGate = new();
    private CancellationTokenSource? _recomputeCts;
    private Task? _currentRecomputeTask;
    private readonly int _debounceMs;
    public ConfigManager(IEnumerable<ConfigRule> rules, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        _rules = rules.ToList();
        _logger = logger ?? NullLogger.Instance;
        _providerRegistry = new ProviderRegistry(_logger, enableDiagnostics: false, factory: providerFactory);
        _repository = new ConfigurationRepository();
        _orchestrator = new ConfigurationOrchestrator(_repository, _logger);
        _subscriptionManager = new ChangeSubscriptionManager(_logger);
        _debounceMs = debounceMilliseconds;
    }

    // --- Public API -------------------------------------------------

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
        if (_repository.TryGetConfiguration<T>(out var jsonElement))
        {
            var registration = _repository.FindRegistration<T>();
            if (registration != null)
            {
                // Deserialize to concrete type, then cast to requested type
                var concreteValue = ConfigurationDeserializer.Deserialize(jsonElement, registration.ConcreteType);
                return (T?)concreteValue;
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
        if (_repository.TryGetConfiguration(type, out var jsonElement))
        {
            var registration = _repository.FindRegistration(type);
            if (registration != null)
            {
                return ConfigurationDeserializer.Deserialize(jsonElement, registration.ConcreteType);
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

    // --- Lifecycle --------------------------------------------------

    public void Dispose()
    {
        _subscriptionManager.Dispose();
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
