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
    private List<ConfigRule> _rules = null!;
    private List<SetupDefinition> _setupDefinitions = null!;
    private readonly List<RuleManager> _ruleManagers = new();

    private ConfigurationAccessor _accessor = null!;
    private ReactiveConfigurationFactory _reactiveFactory = null!;
    private ReactiveConfigManager _reactiveConfigManager = null!;
    private readonly ConfigManagerCapabilityScope _capabilityScope;
    private ConfigurationEngine _engine = null!;
    private ConfigurationState _state = null!;
    private ProviderRegistry _providerRegistry = null!;
    private ExposureRegistry _bindingRegistry = null!;
    private ILogger _logger = NullLogger.Instance;
    private int _debounceMilliseconds = 300;

    private volatile bool _initialized;

    public static ConfigManager Create(Action<ConfigManagerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var manager = new ConfigManager();
        var builder = new ConfigManagerBuilder(manager);
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Creates a bare ConfigManager with only the CapabilityScope initialized.
    /// Must be followed by <see cref="Configure"/> and <see cref="Initialize"/> to be fully operational.
    /// </summary>
    internal ConfigManager()
    {
        _capabilityScope = new ConfigManagerCapabilityScope(this);
        _capabilityScope.Owner.Compose();
    }

    /// <summary>
    /// Configures the ConfigManager with rules, setup, and infrastructure.
    /// Called by <see cref="ConfigManagerBuilder.Build"/> after the user lambda
    /// has had a chance to configure satellite capabilities on the scope.
    /// </summary>
    internal void Configure(
        ConfigRule[] configuredRules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null,
        ILogger? logger = null,
        Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null,
        int debounceMilliseconds = 300)
    {
        // Apply test configuration overrides if present
        _rules = ApplyTestConfigurationOverrides(configuredRules).ToList();

        // Apply test setup overrides if present
        var effectiveSetup = ApplyTestSetupOverrides(setup);
        _setupDefinitions = effectiveSetup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();

        _logger = logger ?? NullLogger.Instance;
        _debounceMilliseconds = debounceMilliseconds;

        _state = new ConfigurationState(_ruleManagers, _rules, _logger);
        _providerRegistry = new ProviderRegistry(_logger, enableDiagnostics: false, factory: providerFactory);
        _bindingRegistry = new ExposureRegistry(_setupDefinitions, _logger, _capabilityScope);

        _accessor = new(_state, _bindingRegistry, _logger);
        _accessor.SetCapabilityScope(_capabilityScope);
        _reactiveConfigManager = new(_logger, _bindingRegistry);
        _reactiveFactory = new(_reactiveConfigManager, _rules, _logger, this, _bindingRegistry);

        _engine = new ConfigurationEngine(_state, _logger);
    }

    internal ConfigManager(Func<RulesBuilder, ConfigRule[]> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
        : this()
    {
        var rulesBuilder = new RulesBuilder();
        var configuredRules = rules(rulesBuilder);
        Configure(configuredRules, setup, logger, providerFactory, debounceMilliseconds);
    }

    internal ConfigManager(IEnumerable<ConfigRule> rules, Func<SetupBuilder, SetupDefinition[]>? setup = null, ILogger? logger = null, Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory = null, int debounceMilliseconds = 300)
        : this()
    {
        Configure(rules.ToArray(), setup, logger, providerFactory, debounceMilliseconds);
    }

    public IReadOnlyList<ConfigRule> Rules => _rules.AsReadOnly();
    internal IReadOnlyList<SetupDefinition> SetupDefinitions => _setupDefinitions.AsReadOnly();

    public ConfigManagerCapabilityScope CapabilityScope => _capabilityScope;


    internal ConfigManager Initialize()
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
                _bindingRegistry,
                _capabilityScope,
                ScheduleRecompute,
                _debounceMilliseconds);

            // Wire up the reactive config manager to use the backplane
            _reactiveConfigManager.SetBackplane(_state.Backplane);
        }
        return this;
    }

    public T GetConfig<T>() => _accessor.GetConfig<T>();
    public bool TryGetConfig<T>(out T? value) => _accessor.TryGetConfig(out value);

#pragma warning disable CS0618 // Type or member is obsolete
    public T GetRequiredConfig<T>() => _accessor.GetRequiredConfig<T>();
#pragma warning restore CS0618

    public object GetConfig(Type type) => _accessor.GetConfig(type);
    public bool TryGetConfig(Type type, out object? value) => _accessor.TryGetConfig(type, out value);

#pragma warning disable CS0618 // Type or member is obsolete
    public object GetRequiredConfig(Type type) => _accessor.GetRequiredConfig(type);
#pragma warning restore CS0618

    public JsonElement? GetConfigAsJson(Type type) => _accessor.GetConfigAsJson(type);

    public IReactiveConfig<T> GetReactiveConfig<T>() => _reactiveFactory.GetReactiveConfig<T>(() => GetConfig<T>());

    public IConfigurationHealthService GetHealthService() => _state.GetHealthService();

    internal void ScheduleRecompute(int startIndex) =>
        _engine.ScheduleRecompute(_ruleManagers, this, startIndex);

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
