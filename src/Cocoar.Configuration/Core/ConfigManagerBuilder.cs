using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Core;

public sealed class ConfigManagerBuilder
{
    private readonly ConfigManager _manager;

    private Func<RulesBuilder, ConfigRule[]>? _rules;
    private IEnumerable<ConfigRule>? _prebuiltRules;
    private Func<SetupBuilder, SetupDefinition[]>? _setup;

    private ILogger? _logger;
    private int _debounceMilliseconds = 300;
    private Func<Type, IProviderConfiguration, ConfigurationProvider>? _providerFactory;
    private IFlagsHealthSource? _flagsHealthSource;

    private readonly List<Action<ConfigManager>> _afterBuildActions = new();

    internal ConfigManagerBuilder(ConfigManager manager)
    {
        _manager = manager;
    }

    /// <summary>
    /// Gets the CapabilityScope of the ConfigManager being built.
    /// Used by satellite libraries to configure capabilities directly on the scope.
    /// </summary>
    public static ConfigManagerCapabilityScope GetCapabilityScope(ConfigManagerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder._manager.CapabilityScope;
    }

    /// <summary>
    /// Gets the <see cref="ConfigManager"/> being built.
    /// Used by same-assembly extensions (e.g. Flags, Secrets) to set data directly on the manager.
    /// </summary>
    internal static ConfigManager GetManager(ConfigManagerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder._manager;
    }

    /// <summary>
    /// Configures the rules that define how configuration is loaded and mapped to types.
    /// </summary>
    /// <param name="rules">A function that builds the configuration rules using the fluent API.</param>
    /// <param name="setup">An optional function to configure DI bindings and type exposure.</param>
    /// <returns>This builder for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.UseConfiguration(
    ///     rules => [
    ///         rules.For&lt;AppSettings&gt;().FromFile("appsettings.json"),
    ///         rules.For&lt;DbSettings&gt;().FromFile("database.json")
    ///     ],
    ///     setup => [
    ///         setup.ConcreteType&lt;AppSettings&gt;()
    ///     ]);
    /// </code>
    /// </example>
    public ConfigManagerBuilder UseConfiguration(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        _rules = rules;
        _prebuiltRules = null;
        _setup = setup;
        return this;
    }

    /// <summary>
    /// Configures the rules using a pre-built collection of <see cref="ConfigRule"/> instances.
    /// Use this overload when rules are constructed programmatically rather than via the fluent builder.
    /// </summary>
    /// <param name="rules">A pre-built collection of configuration rules.</param>
    /// <param name="setup">An optional function to configure DI bindings and type exposure.</param>
    /// <returns>This builder for chaining.</returns>
    public ConfigManagerBuilder UseConfiguration(
        IEnumerable<ConfigRule> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        _prebuiltRules = rules;
        _rules = null;
        _setup = setup;
        return this;
    }

    /// <summary>
    /// Configures the logger used by the configuration system for diagnostics and error reporting.
    /// </summary>
    /// <param name="logger">The logger instance to use.</param>
    /// <returns>This builder for chaining.</returns>
    public ConfigManagerBuilder UseLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Sets the debounce interval for coalescing rapid configuration changes.
    /// When multiple changes occur within this window, only one recompute is triggered.
    /// Default is 300ms.
    /// </summary>
    /// <param name="milliseconds">The debounce interval in milliseconds.</param>
    /// <returns>This builder for chaining.</returns>
    public ConfigManagerBuilder UseDebounce(int milliseconds)
    {
        _debounceMilliseconds = milliseconds;
        return this;
    }

    /// <summary>
    /// Overrides the default provider factory used to instantiate configuration providers.
    /// Intended for testing and advanced scenarios where provider construction needs to be intercepted.
    /// </summary>
    /// <param name="factory">A factory function receiving the provider type and its configuration, returning the provider instance.</param>
    /// <returns>This builder for chaining.</returns>
    public ConfigManagerBuilder UseProviderFactory(
        Func<Type, IProviderConfiguration, ConfigurationProvider> factory)
    {
        _providerFactory = factory;
        return this;
    }

    /// <summary>
    /// Sets the flags health source used by the health reporter to include
    /// expired feature flags in health snapshots.
    /// Called by <c>UseFeatureFlags</c> in <c>Cocoar.Configuration.Flags</c>.
    /// </summary>
    internal ConfigManagerBuilder SetFlagsHealthSource(IFlagsHealthSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _flagsHealthSource = source;
        return this;
    }

    /// <summary>
    /// Registers an action that runs after ConfigManager is fully initialized.
    /// Used by satellite libraries to perform post-init work
    /// (e.g., constructing feature flags).
    /// </summary>
    internal ConfigManagerBuilder AfterBuild(Action<ConfigManager> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _afterBuildActions.Add(action);
        return this;
    }

    internal ConfigManager Build()
    {
        ConfigRule[] configuredRules;

        if (_prebuiltRules is not null)
        {
            configuredRules = _prebuiltRules.ToArray();
        }
        else
        {
            var rulesBuilder = new RulesBuilder();
            configuredRules = (_rules ?? (_ => []))(rulesBuilder);
        }

        _manager.Configure(
            configuredRules,
            _setup,
            _logger,
            _providerFactory,
            _debounceMilliseconds,
            _flagsHealthSource);

        _manager.Initialize();

        foreach (var action in _afterBuildActions)
            action(_manager);

        return _manager;
    }

    internal async Task<ConfigManager> BuildAsync(CancellationToken cancellationToken = default)
    {
        ConfigRule[] configuredRules;

        if (_prebuiltRules is not null)
        {
            configuredRules = _prebuiltRules.ToArray();
        }
        else
        {
            var rulesBuilder = new RulesBuilder();
            configuredRules = (_rules ?? (_ => []))(rulesBuilder);
        }

        _manager.Configure(
            configuredRules,
            _setup,
            _logger,
            _providerFactory,
            _debounceMilliseconds,
            _flagsHealthSource);

        await _manager.InitializeAsync(cancellationToken).ConfigureAwait(false);

        foreach (var action in _afterBuildActions)
            action(_manager);

        return _manager;
    }
}
