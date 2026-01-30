using System.Text.Json;
using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Testing;
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
    /// Automatically detects and applies test configuration overrides when CocoarTestConfiguration is active.
    /// </summary>
    public ConfigManager(Func<RulesBuilder, ConfigRule[]> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        var rulesBuilder = new RulesBuilder();
        var configuredRules = rules(rulesBuilder);

        // Apply test configuration overrides if present
        _rules = ApplyTestConfigurationOverrides(configuredRules).ToList();

        _capabilityScope = new ConfigManagerCapabilityScope(this);
        _capabilityScope.Owner.Compose();

        // Apply test setup overrides if present
        var effectiveSetup = ApplyTestSetupOverrides(setup);
        _setupDefinitions = effectiveSetup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();
        logger ??= NullLogger.Instance;
        _debounceMilliseconds = debounceMilliseconds;

        _state = new ConfigurationState(_ruleManagers, _rules);
        _providerRegistry = new ProviderRegistry(logger, enableDiagnostics: false, factory: providerFactory);
        var bindingRegistry = new ExposureRegistry(_setupDefinitions, logger, _capabilityScope);

        _accessor = new(_state, bindingRegistry, _capabilityScope, logger);
        _reactiveConfigManager = new(logger, bindingRegistry);
        _reactiveFactory = new(_reactiveConfigManager, _rules, logger, this, bindingRegistry);

        _engine = new ConfigurationEngine(_state, logger);
    }

    /// <summary>
    /// Creates a new ConfigManager with a pre-built list of rules.
    /// Automatically detects and applies test configuration overrides when CocoarTestConfiguration is active.
    /// </summary>
    public ConfigManager(IEnumerable<ConfigRule> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
    {
        var configuredRules = rules.ToArray();

        // Apply test configuration overrides if present
        _rules = ApplyTestConfigurationOverrides(configuredRules).ToList();

        _capabilityScope = new ConfigManagerCapabilityScope(this);
        _capabilityScope.Owner.Compose();

        // Apply test setup overrides if present
        var effectiveSetup = ApplyTestSetupOverrides(setup);
        _setupDefinitions = effectiveSetup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();
        logger ??= NullLogger.Instance;
        _debounceMilliseconds = debounceMilliseconds;

        _state = new ConfigurationState(_ruleManagers, _rules);
        _providerRegistry = new ProviderRegistry(logger, enableDiagnostics: false, factory: providerFactory);
        var bindingRegistry = new ExposureRegistry(_setupDefinitions, logger, _capabilityScope);

        _accessor = new(_state, bindingRegistry, _capabilityScope, logger);
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

    /// <summary>
    /// Applies test configuration overrides from AsyncLocal context if present.
    /// Supports both Replace (skip all configured rules) and Append (merge test rules at end) modes.
    /// </summary>
    private static ConfigRule[] ApplyTestConfigurationOverrides(ConfigRule[] configuredRules)
    {
        var testContext = CocoarTestConfiguration.Current;
        if (testContext == null)
        {
            return configuredRules;
        }

        var testRulesBuilder = new RulesBuilder();
        var testRules = testContext.Rules(testRulesBuilder);

        return testContext.Mode switch
        {
            TestConfigurationMode.Replace => testRules,
            TestConfigurationMode.Append => configuredRules.Concat(testRules).ToArray(),
            _ => configuredRules
        };
    }

    /// <summary>
    /// Applies test setup overrides from AsyncLocal context if present.
    /// Test setup is always merged (appended) to configured setup, allowing test-specific
    /// setup options like AllowPlaintext() to override configured settings.
    /// </summary>
    private static Func<SetupBuilder, SetupDefinition[]>? ApplyTestSetupOverrides(
        Func<SetupBuilder, SetupDefinition[]>? configuredSetup)
    {
        var testContext = CocoarTestConfiguration.Current;
        if (testContext?.Setup == null)
        {
            return configuredSetup;
        }

        // Merge: configured setup first, then test setup (last-write-wins for capabilities)
        return builder =>
        {
            var configuredDefs = configuredSetup?.Invoke(builder) ?? [];
            var testDefs = testContext.Setup(builder);
            return [.. configuredDefs, .. testDefs];
        };
    }
}
