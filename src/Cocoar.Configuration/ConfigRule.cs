using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration;

public class ConfigRule(
    Type providerType,
    Func<ConfigManager, ISourceProviderInstanceOptions> providerOptionsFactory,
    Func<ConfigManager, ISourceProviderQueryOptions> queryOptionsFactory,
    ConfigRegistration configContract,
    ConfigRuleOptions? options = null)
{
    // Public surface
    public Type ProviderType { get; } = providerType ?? throw new ArgumentNullException(nameof(providerType));
    public ConfigRegistration Registration { get; } = configContract ?? throw new ArgumentNullException(nameof(configContract));
    public ConfigRuleOptions? Options { get; } = options;

    // Internally, always store factories (instances are wrapped as trivial factories)
    private readonly Func<ConfigManager, ISourceProviderInstanceOptions> _providerOptionsFactory
        = providerOptionsFactory ?? throw new ArgumentNullException(nameof(providerOptionsFactory));
    private readonly Func<ConfigManager, ISourceProviderQueryOptions> _queryOptionsFactory
        = queryOptionsFactory ?? throw new ArgumentNullException(nameof(queryOptionsFactory));

    // Constructor for concrete options (wraps as trivial factories)
    public ConfigRule(
        Type providerType,
        ISourceProviderInstanceOptions providerOptions,
        ISourceProviderQueryOptions queryOptions,
        ConfigRegistration configContract,
        ConfigRuleOptions? options = null)
        : this(
            providerType,
            _ => providerOptions,
            _ => queryOptions,
            configContract,
            options)
    {
        if (providerOptions is null) throw new ArgumentNullException(nameof(providerOptions));
        if (queryOptions is null) throw new ArgumentNullException(nameof(queryOptions));
    }

    public ISourceProviderInstanceOptions ResolveProviderOptions(ConfigManager manager)
        => _providerOptionsFactory(manager);

    public ISourceProviderQueryOptions ResolveQueryOptions(ConfigManager manager)
        => _queryOptionsFactory(manager);

    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(TOptions providerOptions, TQueryOptions queryOptions, ConfigRegistration typeDefinition, Func<bool>? useWhen = null, bool required = true)
        where TProvider : ConfigSourceProvider<TOptions, TQueryOptions>
        where TOptions : ISourceProviderInstanceOptions
        where TQueryOptions : ISourceProviderQueryOptions
    {
        var options = new ConfigRuleOptions
        {
            UseWhen = useWhen,
            Required = required
        };
        return new ConfigRule(typeof(TProvider), providerOptions, queryOptions, typeDefinition, options);
    }

    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(
        Func<ConfigManager, TOptions> providerOptionsFactory,
        Func<ConfigManager, TQueryOptions> queryOptionsFactory,
        ConfigRegistration typeDefinition,
        Func<bool>? useWhen = null,
        bool required = true)
        where TProvider : ConfigSourceProvider<TOptions, TQueryOptions>
        where TOptions : ISourceProviderInstanceOptions
        where TQueryOptions : ISourceProviderQueryOptions
    {
        var options = new ConfigRuleOptions
        {
            UseWhen = useWhen,
            Required = required
        };
        return new ConfigRule(
            typeof(TProvider),
            m => providerOptionsFactory(m),
            m => queryOptionsFactory(m),
            typeDefinition,
            options);
    }

}
