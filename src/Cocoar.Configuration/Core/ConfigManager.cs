using System.Collections.Concurrent;
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
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

public sealed class ConfigManager : IConfigurationAccessor, ITenantConfigurationAccessor, IWritableStoreHost, IDisposable, IAsyncDisposable
{
    private List<SetupDefinition> _setupDefinitions = null!;
    private readonly ConfigManagerCapabilityScope _capabilityScope;
    private ExposureRegistry _bindingRegistry = null!;
    private ILogger _logger = NullLogger.Instance;
    private int _debounceMilliseconds = 300;
    private IFlagsHealthSource? _flagsHealthSource;
    private Func<Type, IProviderConfiguration, ConfigurationProvider>? _providerFactory;

    // The single global pipeline IS "the bundle without a tenant suffix" (ADR-005 §2). Each initialized tenant
    // gets its own TenantPipeline alongside it, layered on the shared global base. The members below forward to
    // the global bundle so existing method bodies are unchanged and the global path stays byte-identical.
    private TenantPipeline _global = null!;

    // Per-tenant pipelines, materialized on demand (ADR-005 §4). The Lazy<Task<...>> gate guarantees a tenant
    // is built exactly once even under concurrent InitializeTenantAsync/EnsureTenantInitializedAsync calls.
    private readonly ConcurrentDictionary<string, Lazy<Task<TenantPipeline>>> _tenants = new();

    private List<ConfigRule> _rules => _global.Rules;
    private List<IRuleManager> _ruleManagers => _global.RuleManagers;
    private ConfigurationAccessor _accessor => _global.Accessor;
    private ReactiveConfigurationFactory _reactiveFactory => _global.ReactiveFactory;
    private ConfigurationEngine _engine => _global.Engine;
    private ConfigurationState _state => _global.State;

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
        var rules = ApplyTestConfigurationOverrides(configuredRules).ToList();

        // Apply test setup overrides if present
        var effectiveSetup = ApplyTestSetupOverrides(setup);
        _setupDefinitions = effectiveSetup?.Invoke(new SetupBuilder(_capabilityScope)).Select(s => s.Build()).ToList() ?? new List<SetupDefinition>();

        _logger = logger ?? NullLogger.Instance;
        _debounceMilliseconds = debounceMilliseconds;

        // Captured so tenant pipelines can be built later with the same health source / provider factory.
        _flagsHealthSource = flagsHealthSource;
        _providerFactory = providerFactory;

        // ExposureRegistry is SHARED (frozen) across the global and all tenant pipelines.
        _bindingRegistry = new ExposureRegistry(_setupDefinitions, _logger, _capabilityScope);

        // Build the global pipeline (rule-suffix = none). It owns its own state/engine/accessor/reactive/rules
        // and borrows the shared scope + binding registry. The global pipeline's recompute accessor is `this`.
        _global = new TenantPipeline(
            rules,
            _capabilityScope,
            _bindingRegistry,
            _logger,
            _debounceMilliseconds,
            flagsHealthSource,
            providerFactory,
            reactiveOwner: this);
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

    /// <summary>
    /// The index of the first service-backed (Layer-2, ADR-006) rule in the global rule list, or <c>-1</c> when
    /// no service-backed rules are configured. The DI activation recompute restores the prefix below this index
    /// (Layer 1 stays stable) and re-runs the suffix once the container's <see cref="IServiceProvider"/> is set.
    /// </summary>
    internal int ServiceBackedLayerStartIndex { get; set; } = -1;

    /// <summary>
    /// Opaque carrier for the DI package's <c>ServiceProviderHolder</c> (kept as <see cref="object"/> so the
    /// No-DI core never names a DI type). Set by <c>UseServiceBackedConfiguration</c>; read back by the DI
    /// package after build to register the holder singleton and wire the activation hosted service.
    /// </summary>
    internal object? ServiceBackedHolder { get; set; }

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

            // The global pipeline's recompute accessor is `this` — byte-identical to before.
            _global.Initialize(this, ScheduleRecompute);
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

            await _global.InitializeAsync(this, ScheduleRecompute, cancellationToken).ConfigureAwait(false);
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
    /// Computes the merged "base" JSON for <paramref name="configType"/> from all rule layers BELOW the
    /// overlay layer identified by <paramref name="isExcludedLayer"/> — i.e. the value the type would have
    /// without that overlay. Used by WritableStore to align override key casing against lower layers and to
    /// report base-vs-effective provenance.
    /// <para>
    /// Thread-safety: this reads each manager's <c>LastJsonContribution</c> without taking the recompute
    /// semaphore — deliberately, because reactive notifications are published <em>inside</em> that semaphore,
    /// so gating here would deadlock a subscriber that writes back. It is safe because a contribution is
    /// written once per recompute and then replaced wholesale (never mutated in place), and the reference
    /// read is atomic: a concurrent recompute can at worst make this observe a one-generation-stale but
    /// internally-consistent contribution, which self-heals on the next read.
    /// </para>
    /// </summary>
    internal MutableJsonObject BuildBaseJson(Type configType, Func<IRuleManager, bool> isExcludedLayer)
    {
        ArgumentNullException.ThrowIfNull(configType);
        ArgumentNullException.ThrowIfNull(isExcludedLayer);

        var merged = new MutableJsonObject();
        foreach (var manager in _ruleManagers)
        {
            if (isExcludedLayer(manager))
            {
                break; // the base is everything strictly below the overlay layer
            }

            if (manager.TypeDefinition != configType)
            {
                continue;
            }

            if (manager.LastJsonContribution is { } contribution)
            {
                MutableJsonMerge.Merge(merged, contribution);
            }
        }

        return merged;
    }

    // IWritableStoreHost — lets the WritableStore adapter compute base/effective JSON against the global pipeline.
    MutableJsonObject IWritableStoreHost.BuildBaseJson(Type configType, Func<IRuleManager, bool> isExcludedLayer)
        => BuildBaseJson(configType, isExcludedLayer);

    JsonElement? IWritableStoreHost.GetConfigAsJson(Type type) => GetConfigAsJson(type);

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
    /// Runs a recompute of the GLOBAL pipeline from <paramref name="startIndex"/> directly to completion (under the
    /// recompute semaphore), NOT via the cancel-on-reschedule scheduler. Used by the DI Layer-2 activation so a
    /// concurrent provider-change signal cannot cancel activation before Layer 2 has committed (ADR-006 §7 readiness).
    /// </summary>
    internal Task RecomputeNowAsync(int startIndex, CancellationToken cancellationToken = default) =>
        _engine.RecomputeAndUpdateHealthAsync(_ruleManagers, this, startIndex, cancellationToken);

    /// <summary>
    /// Runs the same direct recompute on every already-initialized tenant pipeline from <paramref name="startIndex"/>.
    /// Used on Layer-2 activation so tenants built before the container was published (their sp-gated rules were
    /// skipped at init) pick up their service-backed values. Each tenant degrades independently.
    /// </summary>
    internal async Task RecomputeInitializedTenantsNowAsync(int startIndex, CancellationToken cancellationToken = default)
    {
        foreach (var lazy in _tenants.Values)
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            var task = lazy.Value;
            if (!task.IsCompletedSuccessfully)
            {
                continue;
            }

            var pipeline = task.Result;
            if (!pipeline.IsInitialized)
            {
                continue;
            }

            try
            {
                // RecomputeAndUpdateHealthAsync swallows recompute failures; the per-tenant guard here additionally
                // isolates the narrow dispose-race (a tenant removed mid-fan-out can surface ObjectDisposed /
                // index races from its health update) so one removed/faulting tenant never blocks the others.
                await pipeline.Engine.RecomputeAndUpdateHealthAsync(
                    pipeline.RuleManagers, pipeline.Accessor, startIndex, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Isolated: this tenant self-heals on its next provider change.
            }
        }
    }

    // ===== Tenant lifecycle (ADR-005 §4/§5, ITenantConfigurationAccessor) =====

    /// <inheritdoc />
    public Task InitializeTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        => GetOrBuildTenantAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public Task EnsureTenantInitializedAsync(string tenantId, CancellationToken cancellationToken = default)
        => GetOrBuildTenantAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public bool IsTenantInitialized(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return _tenants.TryGetValue(tenantId, out var lazy)
            && lazy.IsValueCreated
            && lazy.Value.IsCompletedSuccessfully
            && lazy.Value.Result.IsInitialized;
    }

    /// <inheritdoc />
    public T? GetConfigForTenant<T>(string tenantId) where T : class
        => GetInitializedTenantOrThrow(tenantId).Accessor.GetConfig<T>();

    /// <inheritdoc />
    public IReactiveConfig<T> GetReactiveConfigForTenant<T>(string tenantId)
    {
        var pipeline = GetInitializedTenantOrThrow(tenantId);
        // Mirror the global GetReactiveConfig<T>: the factory + ReactiveConfigManager are the tenant pipeline's
        // own, and the value source reads the tenant accessor — so the reactive tracks THIS tenant's value.
        return pipeline.ReactiveFactory.GetReactiveConfig<T>(() => (T)pipeline.Accessor.GetConfig(typeof(T)));
    }

    /// <inheritdoc />
    public async Task RemoveTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!_tenants.TryRemove(tenantId, out var lazy))
        {
            return;
        }

        TenantPipeline pipeline;
        try
        {
            // Always resolve the removed entry before disposing — even if its build had not been triggered yet.
            // Accessing lazy.Value forces the (single) build to run if a concurrent initializer hadn't started it,
            // so a pipeline that gets built after this removal cannot be left orphaned and never disposed.
            pipeline = await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            return; // init faulted/cancelled — nothing was published, nothing to dispose
        }

        await pipeline.DisposeAsync().ConfigureAwait(false);
    }

    private Task<TenantPipeline> GetOrBuildTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (_initialized == 0)
        {
            throw new InvalidOperationException(
                "ConfigManager has not been initialized. Tenants can only be initialized after the global pipeline is ready.");
        }

        var lazy = _tenants.GetOrAdd(
            tenantId,
            id => new Lazy<Task<TenantPipeline>>(() => BuildTenantAsync(id, cancellationToken)));
        var task = lazy.Value;

        // If a PREVIOUS build for this tenant completed-faulted, evict that exact entry (identity-checked, so we
        // never drop a healthy retry inserted under the same key) and rebuild once. An in-flight build is returned
        // as-is. Bounded to a single rebuild so an always-failing build can't spin.
        if (task.IsCompleted && (task.IsFaulted || task.IsCanceled))
        {
            _tenants.TryRemove(new KeyValuePair<string, Lazy<Task<TenantPipeline>>>(tenantId, lazy));
            lazy = _tenants.GetOrAdd(
                tenantId,
                id => new Lazy<Task<TenantPipeline>>(() => BuildTenantAsync(id, cancellationToken)));
            task = lazy.Value;
        }

        return task;
    }

    private async Task<TenantPipeline> BuildTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        // Same flat rule list as the global pipeline; the tenant pipeline owns its own state/engine/accessor/
        // reactive/rule-managers and borrows the shared (frozen) capability scope + binding registry.
        var pipeline = new TenantPipeline(
            _global.Rules,
            _capabilityScope,
            _bindingRegistry,
            _logger,
            _debounceMilliseconds,
            _flagsHealthSource,
            _providerFactory,
            reactiveOwner: this,
            tenantId: tenantId);

        try
        {
            // Recompute uses the pipeline's OWN accessor (Tenant = id): .TenantScoped() rules run and tenant-varying
            // factories interpolate the id. Each tenant runs the FULL flat rule list with its own provider instances
            // and own change subscriptions. This is the deliberate v1 model (ADR-005 §6): it is correct AND gives
            // automatic fan-out — a live global base source (file/observable/http) propagates to every initialized
            // tenant through that tenant's own subscription, with no cross-pipeline coordinator and none of the
            // lock-ordering hazards a shared seed-from-global path would carry. The trade-off is linear resource use
            // (N tenants re-run the base); the seed-from-global sharing optimization is a documented, deferred TODO.
            await pipeline.InitializeAsync(
                pipeline.Accessor,
                startIndex => pipeline.Engine.ScheduleRecompute(pipeline.RuleManagers, pipeline.Accessor, startIndex),
                cancellationToken).ConfigureAwait(false);

            return pipeline;
        }
        catch
        {
            // Dispose the partially-built pipeline so a failed init leaks nothing. The faulted task stays cached
            // until GetOrBuildTenantAsync evicts it (identity-checked) on the next call — no self-eviction here,
            // which would risk an ABA race against a healthy retry inserted under the same key.
            await pipeline.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private TenantPipeline GetInitializedTenantOrThrow(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (_tenants.TryGetValue(tenantId, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
        {
            return lazy.Value.Result;
        }

        throw new InvalidOperationException(
            $"Tenant '{tenantId}' is not initialized. Call InitializeTenantAsync/EnsureTenantInitializedAsync first.");
    }

    /// <summary>
    /// The initialized tenant pipeline, for in-assembly facades (e.g. per-tenant WritableStore) that need the
    /// tenant's own rule managers/host. Throws if the tenant is not initialized.
    /// </summary>
    internal TenantPipeline GetInitializedTenantPipeline(string tenantId) => GetInitializedTenantOrThrow(tenantId);

    /// <summary>
    /// Disposes the configuration manager and all associated resources.
    /// After disposal, configuration methods will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public void Dispose()
    {
        foreach (var lazy in _tenants.Values)
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
            {
                Safety.DisposeQuietly(lazy.Value.Result);
            }
        }
        _tenants.Clear();

        _global?.Dispose();
        Interlocked.Exchange(ref _initialized, 0);
    }

    /// <summary>
    /// Asynchronously disposes the configuration manager, awaiting any in-flight recompute to finish.
    /// Preferred over <see cref="Dispose"/> in ASP.NET Core and other async hosts, which call
    /// <see cref="IAsyncDisposable.DisposeAsync"/> on singletons at shutdown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _tenants.Values)
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            TenantPipeline? pipeline = null;
            try
            {
                pipeline = await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                // faulted/cancelled init — nothing to dispose
            }

            if (pipeline != null)
            {
                await pipeline.DisposeAsync().ConfigureAwait(false);
            }
        }
        _tenants.Clear();

        if (_global != null) await _global.DisposeAsync().ConfigureAwait(false);
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
