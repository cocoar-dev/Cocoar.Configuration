using System.Text.Json;
using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Fluent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Core;

public class ConfigManager : IConfigurationAccessor, IDisposable
{
    private readonly List<ConfigRule> _rules;
    private readonly List<SetupDefinition> _setupDefinitions;
    private readonly List<RuleManager> _ruleManagers = new();

    private readonly ConfigurationAccessor _accessor;
    private readonly ConfigurationHealthTracker _healthTracker;
    private readonly ReactiveConfigurationFactory _reactiveFactory;
    private readonly ConfigurationInitializer _initializer;
    private readonly ConfigurationRecomputeCoordinator _recomputeCoordinator;
    private readonly ReactiveConfigManager _reactiveConfigManager;
    private readonly CapabilityScope _capabilityScope = new();

    private volatile bool _initialized;

    /// <summary>
    /// Creates a new ConfigManager with a function-based rules builder.
    /// </summary>
    public ConfigManager(Func<RulesBuilder, ConfigRule[]> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        var rulesBuilder = new RulesBuilder();
        _rules = rules(rulesBuilder).ToList();
        _setupDefinitions = setup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();
        logger ??= NullLogger.Instance;
        
        var repository = new ConfigurationRepository();
        var providerRegistry = new ProviderRegistry(logger, enableDiagnostics: false, factory: providerFactory);
        var bindingRegistry = new ExposureRegistry(_setupDefinitions, logger, _capabilityScope);
        
        _accessor = new(repository, bindingRegistry);
        _healthTracker = new(_ruleManagers, _rules);
        _reactiveConfigManager = new(logger, bindingRegistry);
    _reactiveFactory = new(_reactiveConfigManager, _rules, logger, this, bindingRegistry);
        
        var orchestrator = new ConfigurationOrchestrator(repository, logger);
        var subscriptionManager = new ChangeSubscriptionManager(logger);
        
        _recomputeCoordinator = new(
            _ruleManagers, orchestrator, _reactiveConfigManager, _healthTracker, logger);
        
        _initializer = new(
            _rules, _ruleManagers, providerRegistry, orchestrator, 
            subscriptionManager, _healthTracker, logger, debounceMilliseconds);
    }

    /// <summary>
    /// Creates a new ConfigManager with a pre-built list of rules.
    /// </summary>
    public ConfigManager(IEnumerable<ConfigRule> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        _rules = rules.ToList();
        _setupDefinitions = setup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();
        logger ??= NullLogger.Instance;
        
        var repository = new ConfigurationRepository();
        var providerRegistry = new ProviderRegistry(logger, enableDiagnostics: false, factory: providerFactory);
        var bindingRegistry = new ExposureRegistry(_setupDefinitions, logger, _capabilityScope);
        
        _accessor = new(repository, bindingRegistry);
        _healthTracker = new(_ruleManagers, _rules);
        _reactiveConfigManager = new(logger, bindingRegistry);
    _reactiveFactory = new(_reactiveConfigManager, _rules, logger, this, bindingRegistry);
        
        var orchestrator = new ConfigurationOrchestrator(repository, logger);
        var subscriptionManager = new ChangeSubscriptionManager(logger);
        
        _recomputeCoordinator = new(
            _ruleManagers, orchestrator, _reactiveConfigManager, _healthTracker, logger);
        
        _initializer = new(
            _rules, _ruleManagers, providerRegistry, orchestrator, 
            subscriptionManager, _healthTracker, logger, debounceMilliseconds);
    }

    public IReadOnlyList<ConfigRule> Rules => _rules.AsReadOnly();
    internal IReadOnlyList<SetupDefinition> SetupDefinitions => _setupDefinitions.AsReadOnly();
    
    public CapabilityScope CapabilityScope => _capabilityScope;


    public ConfigManager Initialize()
    {
        if (_initialized)
        {
            return this;
        }

        if (!Interlocked.CompareExchange(ref _initialized, true, false))
        {
            _initializer.Initialize(this, ScheduleRecompute);
        }
        return this;
    }

    public T? GetConfig<T>() => _accessor.GetConfig<T>();
    public bool TryGetConfig<T>(out T? value) => _accessor.TryGetConfig(out value);
    public T GetRequiredConfig<T>() => _accessor.GetRequiredConfig<T>();
    public object? GetConfig(Type type) => _accessor.GetConfig(type);
    public bool TryGetConfig(Type type, out object? value) => _accessor.TryGetConfig(type, out value);
    public object GetRequiredConfig(Type type) => _accessor.GetRequiredConfig(type);
    public JsonElement? GetConfigAsJson(Type type) => _accessor.GetConfigAsJson(type);

    public IReactiveConfig<T> GetReactiveConfig<T>() => _reactiveFactory.GetReactiveConfig(() => GetConfig<T>()!);

    public IConfigurationHealthService GetHealthService() => _healthTracker.GetHealthService();

    internal void ScheduleRecompute(int startIndex) => _recomputeCoordinator.ScheduleRecompute(startIndex, this);
    

    internal Task? CurrentRecomputeTask => _recomputeCoordinator.CurrentRecomputeTask;

    public void Dispose()
    {
        _recomputeCoordinator.Dispose();
        _reactiveConfigManager.Dispose();
        _healthTracker.Dispose();
        
        foreach (var rm in _ruleManagers.ToArray())
        {
            Safety.DisposeQuietly(rm);
        }

        _ruleManagers.Clear();
        _initialized = false;
        GC.SuppressFinalize(this);
    }
}
