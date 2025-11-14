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
    private readonly ReactiveConfigurationFactory _reactiveFactory;
    private readonly ReactiveConfigManager _reactiveConfigManager;
    private readonly ConfigManagerCapabilityScope _capabilityScope;
    private readonly ConfigurationEngine _engine;
    private readonly ConfigurationState _state;
    private readonly ProviderRegistry _providerRegistry;
    private readonly int _debounceMilliseconds;

    private volatile bool _initialized;

    /// <summary>
    /// Creates a new ConfigManager with a function-based rules builder.
    /// </summary>
    public ConfigManager(Func<RulesBuilder, ConfigRule[]> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        var rulesBuilder = new RulesBuilder();
        _rules = rules(rulesBuilder).ToList();

        _capabilityScope = new ConfigManagerCapabilityScope(this);

        // Create global Owner composer for cross-cutting capabilities (e.g., Secrets)
        _capabilityScope.Owner.Compose();
        
        _setupDefinitions = setup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();
        logger ??= NullLogger.Instance;
        _debounceMilliseconds = debounceMilliseconds;

        _state = new ConfigurationState(_ruleManagers, _rules);
        _providerRegistry = new ProviderRegistry(logger, enableDiagnostics: false, factory: providerFactory);
        var bindingRegistry = new ExposureRegistry(_setupDefinitions, logger, _capabilityScope);

        _accessor = new(_state, bindingRegistry, _capabilityScope);
        _reactiveConfigManager = new(logger, bindingRegistry);
        _reactiveFactory = new(_reactiveConfigManager, _rules, logger, this, bindingRegistry);

        _engine = new ConfigurationEngine(_state, logger);
    }

    /// <summary>
    /// Creates a new ConfigManager with a pre-built list of rules.
    /// </summary>
    public ConfigManager(IEnumerable<ConfigRule> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        _rules = rules.ToList();

        _capabilityScope = new ConfigManagerCapabilityScope(this);

        // Create global Owner composer for cross-cutting capabilities (e.g., Secrets)
        _capabilityScope.Owner.Compose();
        
        _setupDefinitions = setup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();
        logger ??= NullLogger.Instance;
        _debounceMilliseconds = debounceMilliseconds;

        _state = new ConfigurationState(_ruleManagers, _rules);
        _providerRegistry = new ProviderRegistry(logger, enableDiagnostics: false, factory: providerFactory);
        var bindingRegistry = new ExposureRegistry(_setupDefinitions, logger, _capabilityScope);

        _accessor = new(_state, bindingRegistry, _capabilityScope);
        _reactiveConfigManager = new(logger, bindingRegistry);
        _reactiveFactory = new(_reactiveConfigManager, _rules, logger, this, bindingRegistry);

        _engine = new ConfigurationEngine(_state, logger);
    }

    public IReadOnlyList<ConfigRule> Rules => _rules.AsReadOnly();
    internal IReadOnlyList<SetupDefinition> SetupDefinitions => _setupDefinitions.AsReadOnly();

    public ConfigManagerCapabilityScope CapabilityScope => _capabilityScope;


    public ConfigManager Initialize()
    {
        if (_initialized)
        {
            return this;
        }

        if (!Interlocked.CompareExchange(ref _initialized, true, false))
        {
            _capabilityScope.Owner.TryGetComposer(out var composer);
            composer?.Build();
            _capabilityScope.Owner.GetComposition()?.UsingEach<IDeferredConfiguration>(c => c.Apply());
            
            _engine.InitializeAndCompute(
                _rules,
                _ruleManagers,
                _providerRegistry,
                this,
                ScheduleRecompute,
                _debounceMilliseconds);
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

    public IConfigurationHealthService GetHealthService() => _state.GetHealthService();

    internal void ScheduleRecompute(int startIndex) => 
        _engine.ScheduleRecompute(_ruleManagers, this, _reactiveConfigManager, startIndex);

    internal Task? CurrentRecomputeTask => _engine.CurrentRecomputeTask;

    public void Dispose()
    {
        _engine.Dispose();
        _reactiveConfigManager.Dispose();
        _state.Dispose();

        foreach (var rm in _ruleManagers.ToArray())
        {
            Safety.DisposeQuietly(rm);
        }

        _ruleManagers.Clear();
        _initialized = false;
        GC.SuppressFinalize(this);
    }
}
