using System.Text.Json;
using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Flags.Internal;
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

public sealed class ConfigManager : IConfigurationAccessor, IDisposable, IAsyncDisposable
{
    private List<ConfigRule> _rules = null!;
    private List<SetupDefinition> _setupDefinitions = null!;
    private readonly List<IRuleManager> _ruleManagers = new();

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

    private int _initialized;

    /// <summary>
    /// Creates and initializes a new <see cref="ConfigManager"/> using the provided configuration.
    /// </summary>
    /// <param name="configure">An action to configure the <see cref="ConfigManagerBuilder"/>.</param>
    /// <returns>A fully initialized <see cref="ConfigManager"/> ready for use.</returns>
    /// <example>
    /// <code>
    /// var manager = ConfigManager.Create(builder => builder
    ///     .UseConfiguration(rules => [
    ///         rules.For&lt;AppSettings&gt;().FromFile("appsettings.json")
    ///     ]));
    /// var settings = manager.GetConfig&lt;AppSettings&gt;();
    /// </code>
    /// </example>
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
        int debounceMilliseconds = 300,
        IFlagsHealthSource? flagsHealthSource = null)
    {
        // Apply test configuration overrides if present
        _rules = ApplyTestConfigurationOverrides(configuredRules).ToList();

        // Apply test setup overrides if present
        var effectiveSetup = ApplyTestSetupOverrides(setup);
        _setupDefinitions = effectiveSetup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();

        _logger = logger ?? NullLogger.Instance;
        _debounceMilliseconds = debounceMilliseconds;

        _state = new ConfigurationState(_ruleManagers, _rules, _logger, flagsHealthSource);
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

    internal ConfigManagerCapabilityScope CapabilityScope => _capabilityScope;

    /// <summary>
    /// Set by <c>UseFeatureFlags</c>. Null when feature flags have not been configured.
    /// </summary>
    internal FlagsSetupData? FlagsSetup { get; set; }

    /// <summary>
    /// Set by <c>UseEntitlements</c>. Null when entitlements have not been configured.
    /// </summary>
    internal EntitlementsSetupData? EntitlementsSetup { get; set; }
    internal MasterBackplane Backplane => _state.Backplane;

    internal ConfigManager Initialize()
    {
        if (_initialized != 0)
        {
            return this;
        }

        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
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

    internal async Task<ConfigManager> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized != 0)
            return this;

        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            _capabilityScope.Owner.TryGetComposer(out var composer);
            composer?.Build();
            _capabilityScope.Owner.GetComposition()?.UsingEach<IDeferredConfiguration>(c => c.Apply());

            await _engine.InitializeAndComputeAsync(
                _rules,
                _ruleManagers,
                _providerRegistry,
                this,
                _bindingRegistry,
                _capabilityScope,
                ScheduleRecompute,
                _debounceMilliseconds,
                cancellationToken).ConfigureAwait(false);

            _reactiveConfigManager.SetBackplane(_state.Backplane);
        }
        return this;
    }

    /// <summary>
    /// Creates and initializes a new <see cref="ConfigManager"/> asynchronously.
    /// Prefer this over <see cref="Create"/> in console apps or any context where
    /// blocking the calling thread during provider I/O is undesirable.
    /// </summary>
    /// <param name="configure">An action to configure the <see cref="ConfigManagerBuilder"/>.</param>
    /// <param name="cancellationToken">Token to cancel the initialization.</param>
    public static async Task<ConfigManager> CreateAsync(
        Action<ConfigManagerBuilder> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var manager = new ConfigManager();
        var builder = new ConfigManagerBuilder(manager);
        configure(builder);
        return await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a configuration instance of the specified type from the current snapshot.
    /// </summary>
    /// <typeparam name="T">The configuration type to retrieve. Must be a class.</typeparam>
    /// <returns>The configuration instance, or null if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Configure"/> has not been called.</exception>
    public T? GetConfig<T>() where T : class
    {
        if (_initialized == 0) throw new InvalidOperationException("ConfigManager has not been initialized. Call Configure() first.");
        return _accessor.GetConfig<T>();
    }

    /// <summary>
    /// Attempts to get a configuration instance without throwing.
    /// </summary>
    /// <typeparam name="T">The configuration type to retrieve. Must be a class.</typeparam>
    /// <param name="value">The configuration instance if found; otherwise null.</param>
    /// <returns>True if the configuration exists; false otherwise.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Configure"/> has not been called.</exception>
    public bool TryGetConfig<T>(out T? value) where T : class
    {
        if (_initialized == 0) throw new InvalidOperationException("ConfigManager has not been initialized. Call Configure() first.");
        return _accessor.TryGetConfig(out value);
    }

#pragma warning disable CS0618 // Type or member is obsolete
    public T GetRequiredConfig<T>() => _accessor.GetRequiredConfig<T>();
#pragma warning restore CS0618

    /// <inheritdoc cref="GetConfig{T}"/>
    public object GetConfig(Type type) => _accessor.GetConfig(type);

    /// <inheritdoc cref="TryGetConfig{T}(out T?)"/>
    public bool TryGetConfig(Type type, out object? value) => _accessor.TryGetConfig(type, out value);

#pragma warning disable CS0618 // Type or member is obsolete
    /// <inheritdoc cref="GetRequiredConfig{T}"/>
    public object GetRequiredConfig(Type type) => _accessor.GetRequiredConfig(type);
#pragma warning restore CS0618

    /// <summary>
    /// Gets the current configuration snapshot for the specified type serialized as a <see cref="JsonElement"/>.
    /// Returns <c>null</c> if no rule is registered for the type.
    /// </summary>
    /// <param name="type">The configuration type to retrieve.</param>
    public JsonElement? GetConfigAsJson(Type type) => _accessor.GetConfigAsJson(type);

    /// <summary>
    /// Gets a reactive wrapper for the specified configuration type.
    /// The returned <see cref="IReactiveConfig{T}"/> emits the current value immediately on subscribe
    /// and then on every subsequent configuration change (replay-1 / BehaviorSubject semantics).
    /// </summary>
    /// <typeparam name="T">The configuration type. Must be a class, interface (with ExposeAs), or ValueTuple.</typeparam>
    /// <returns>A reactive configuration wrapper.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Configure"/> has not been called.</exception>
    public IReactiveConfig<T> GetReactiveConfig<T>()
    {
        if (_initialized == 0) throw new InvalidOperationException("ConfigManager has not been initialized. Call Configure() first.");
        return _reactiveFactory.GetReactiveConfig<T>(() => (T)GetConfig(typeof(T)));
    }

    /// <summary>Current overall health status of the configuration system.</summary>
    public HealthStatus HealthStatus
    {
        get
        {
            if (_initialized == 0) throw new InvalidOperationException("ConfigManager has not been initialized. Call Configure() first.");
            return _state.HealthStatus;
        }
    }

    /// <summary><c>true</c> when <see cref="HealthStatus"/> is <see cref="Health.HealthStatus.Healthy"/>.</summary>
    public bool IsHealthy
    {
        get
        {
            if (_initialized == 0) throw new InvalidOperationException("ConfigManager has not been initialized. Call Configure() first.");
            return _state.IsHealthy;
        }
    }

    /// <summary>Human-readable description of the current health state (e.g. "1 required rule(s) failed").</summary>
    internal string HealthDescription => _state.HealthDescription;

    internal void ScheduleRecompute(int startIndex) =>
        _engine.ScheduleRecompute(_ruleManagers, this, startIndex);

    internal Task? CurrentRecomputeTask => _engine.CurrentRecomputeTask;

    /// <summary>
    /// Disposes the configuration manager and all associated resources.
    /// After disposal, configuration methods will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public void Dispose()
    {
        _engine?.Dispose();
        _reactiveConfigManager?.Dispose();
        _state?.Dispose();

        foreach (var rm in _ruleManagers.ToArray())
        {
            Safety.DisposeQuietly(rm);
        }

        _ruleManagers.Clear();
        Interlocked.Exchange(ref _initialized, 0);
    }

    /// <summary>
    /// Asynchronously disposes the configuration manager, awaiting any in-flight recompute to finish.
    /// Preferred over <see cref="Dispose"/> in ASP.NET Core and other async hosts, which call
    /// <see cref="IAsyncDisposable.DisposeAsync"/> on singletons at shutdown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_engine != null) await _engine.DisposeAsync().ConfigureAwait(false);
        _reactiveConfigManager?.Dispose();
        _state?.Dispose();

        foreach (var rm in _ruleManagers.ToArray())
        {
            Safety.DisposeQuietly(rm);
        }

        _ruleManagers.Clear();
        Interlocked.Exchange(ref _initialized, 0);
    }

    /// <summary>
    /// Applies test configuration overrides from AsyncLocal context if present.
    /// Supports both Replace (skip all configured rules) and Append (merge test rules at end) modes.
    /// When <see cref="TestConfigurationContext.ConfigurationMode"/> is null no rules override is applied.
    /// </summary>
    private static ConfigRule[] ApplyTestConfigurationOverrides(ConfigRule[] configuredRules)
    {
        var testContext = CocoarTestConfiguration.Current;
        if (testContext?.Rules == null || testContext.ConfigurationMode == null)
            return configuredRules;

        var testRulesBuilder = new RulesBuilder();
        var testRules = testContext.Rules(testRulesBuilder);

        return testContext.ConfigurationMode switch
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
#if NET9_0_OR_GREATER
            return [.. configuredDefs, .. testDefs];
#else
            return configuredDefs.Concat(testDefs).ToArray();
#endif
        };
    }
}
