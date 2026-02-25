using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Fluent;
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

    public ConfigManagerBuilder WithConfiguration(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        _rules = rules;
        _prebuiltRules = null;
        _setup = setup;
        return this;
    }

    public ConfigManagerBuilder WithConfiguration(
        IEnumerable<ConfigRule> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        _prebuiltRules = rules;
        _rules = null;
        _setup = setup;
        return this;
    }

    public ConfigManagerBuilder UseLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    public ConfigManagerBuilder UseDebounce(int milliseconds)
    {
        _debounceMilliseconds = milliseconds;
        return this;
    }

    public ConfigManagerBuilder UseProviderFactory(
        Func<Type, IProviderConfiguration, ConfigurationProvider> factory)
    {
        _providerFactory = factory;
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
            _debounceMilliseconds);

        _manager.Initialize();

        foreach (var action in _afterBuildActions)
            action(_manager);

        return _manager;
    }
}
