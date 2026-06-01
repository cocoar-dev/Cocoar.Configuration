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

/// <summary>
/// A self-contained configuration pipeline bundle: state, engine, accessor, reactive factory, provider
/// registry and rule managers. The single global pipeline and (later) each tenant pipeline are instances
/// of this same type — "global" is simply the bundle without a tenant suffix.
/// <para>
/// Shared, read-only infrastructure (the capability scope and the frozen <see cref="ExposureRegistry"/>)
/// is injected and never owned/disposed here — it is owned by the <see cref="ConfigManager"/> and shared
/// across all pipelines (ADR-005 §2). Each pipeline owns its own state/engine/accessor/reactive/rules.
/// </para>
/// </summary>
internal sealed class TenantPipeline : IWritableStoreHost, IDisposable, IAsyncDisposable
{
    private readonly ConfigManagerCapabilityScope _capabilityScope; // shared (read-only after Initialize)
    private readonly ExposureRegistry _bindingRegistry;             // shared (frozen)
    private readonly ILogger _logger;                              // shared
    private readonly int _debounceMilliseconds;                   // shared value

    internal List<ConfigRule> Rules { get; }
    internal List<IRuleManager> RuleManagers { get; } = new();
    internal ConfigurationState State { get; }
    internal ProviderRegistry ProviderRegistry { get; }
    internal ConfigurationAccessor Accessor { get; }
    internal ReactiveConfigManager ReactiveConfigManager { get; }
    internal ReactiveConfigurationFactory ReactiveFactory { get; }
    internal ConfigurationEngine Engine { get; }

    private int _initialized;
    internal bool IsInitialized => Volatile.Read(ref _initialized) != 0;

    /// <summary>The tenant this pipeline resolves for, or <c>null</c> for the global (tenant-agnostic) pipeline.</summary>
    internal string? TenantId { get; }

    internal TenantPipeline(
        List<ConfigRule> rules,
        ConfigManagerCapabilityScope capabilityScope,
        ExposureRegistry bindingRegistry,
        ILogger logger,
        int debounceMilliseconds,
        IFlagsHealthSource? flagsHealthSource,
        Func<Type, IProviderConfiguration, ConfigurationProvider>? providerFactory,
        ConfigManager reactiveOwner,
        string? tenantId = null)
    {
        Rules = rules;
        _capabilityScope = capabilityScope;
        _bindingRegistry = bindingRegistry;
        _logger = logger;
        _debounceMilliseconds = debounceMilliseconds;
        TenantId = tenantId;

        // Construction order is byte-identical to the former ConfigManager.Configure sequence.
        // The accessor carries the tenant id so .TenantScoped() rules run (and tenant-varying factories
        // interpolate it) in a tenant pipeline, while the global pipeline keeps tenant == null.
        State = new ConfigurationState(RuleManagers, Rules, _logger, flagsHealthSource);
        ProviderRegistry = new ProviderRegistry(_logger, enableDiagnostics: false, factory: providerFactory);
        Accessor = new ConfigurationAccessor(State, _bindingRegistry, _logger, Rules, tenantId);
        Accessor.SetCapabilityScope(_capabilityScope);
        ReactiveConfigManager = new ReactiveConfigManager(_logger, _bindingRegistry);

        // Reactive reads bind to the global ConfigManager for the global pipeline (byte-identical) and to this
        // pipeline's OWN accessor for a tenant pipeline; the backplane is always this pipeline's own (the global
        // ConfigManager's backplane IS the global pipeline's State.Backplane, so global stays byte-identical).
        IConfigurationAccessor reactiveAccessor = tenantId is null ? reactiveOwner : Accessor;
        ReactiveFactory = new ReactiveConfigurationFactory(
            ReactiveConfigManager, Rules, _logger, reactiveAccessor, () => State.Backplane, _bindingRegistry);
        Engine = new ConfigurationEngine(State, _logger);
    }

    /// <param name="recomputeAccessor">
    /// The <see cref="IConfigurationAccessor"/> handed to the engine for recompute-window fallback reads and
    /// provider option factories. For the global pipeline this is the owning <see cref="ConfigManager"/>
    /// (byte-identical to before); for a tenant pipeline it is the tenant's own accessor.
    /// </param>
    internal void Initialize(IConfigurationAccessor recomputeAccessor, Action<int> scheduleRecompute)
    {
        Engine.InitializeAndCompute(
            Rules, RuleManagers, ProviderRegistry, recomputeAccessor,
            _bindingRegistry, _capabilityScope, scheduleRecompute, _debounceMilliseconds);
        ReactiveConfigManager.SetBackplane(State.Backplane);
        Volatile.Write(ref _initialized, 1);
    }

    internal async Task InitializeAsync(IConfigurationAccessor recomputeAccessor, Action<int> scheduleRecompute, CancellationToken cancellationToken)
    {
        await Engine.InitializeAndComputeAsync(
            Rules, RuleManagers, ProviderRegistry, recomputeAccessor,
            _bindingRegistry, _capabilityScope, scheduleRecompute, _debounceMilliseconds, cancellationToken).ConfigureAwait(false);
        ReactiveConfigManager.SetBackplane(State.Backplane);
        Volatile.Write(ref _initialized, 1);
    }

    // IWritableStoreHost — a per-tenant WritableStore overlay computes its base/effective JSON against THIS
    // pipeline's own rule managers and snapshot (mirrors ConfigManager.BuildBaseJson over the global managers).
    public MutableJsonObject BuildBaseJson(Type configType, Func<IRuleManager, bool> isExcludedLayer)
    {
        ArgumentNullException.ThrowIfNull(configType);
        ArgumentNullException.ThrowIfNull(isExcludedLayer);

        var merged = new MutableJsonObject();
        foreach (var manager in RuleManagers)
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
                MutableJsonMerge.Merge(merged, contribution, ConfigMergeOptions.CaseInsensitive);
            }
        }

        return merged;
    }

    public JsonElement? GetConfigAsJson(Type type) => State.GetConfigurationAsJson(type);

    public void Dispose()
    {
        Engine?.Dispose();
        ReactiveConfigManager?.Dispose();
        State?.Dispose();

        foreach (var rm in RuleManagers.ToArray())
            Safety.DisposeQuietly(rm);

        RuleManagers.Clear();
        Volatile.Write(ref _initialized, 0);
    }

    public async ValueTask DisposeAsync()
    {
        if (Engine != null) await Engine.DisposeAsync().ConfigureAwait(false);
        ReactiveConfigManager?.Dispose();
        State?.Dispose();

        foreach (var rm in RuleManagers.ToArray())
            Safety.DisposeQuietly(rm);

        RuleManagers.Clear();
        Volatile.Write(ref _initialized, 0);
    }
}
