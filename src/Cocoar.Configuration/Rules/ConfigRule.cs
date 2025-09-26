using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Rules;

public class ConfigRule(
    Type providerType,
    Func<IConfigurationAccessor, IProviderConfiguration> providerOptionsFactory,
    Func<IConfigurationAccessor, IProviderQuery> queryOptionsFactory,
    Type concreteType,
    ConfigRuleOptions? options = null)
{

    public Type ProviderType { get; } = providerType ?? throw new ArgumentNullException(nameof(providerType));
    public Type ConcreteType { get; } = concreteType ?? throw new ArgumentNullException(nameof(concreteType));
    public ConfigRuleOptions? Options { get; } = options;


    private readonly Func<IConfigurationAccessor, IProviderConfiguration> _providerOptionsFactory
        = providerOptionsFactory ?? throw new ArgumentNullException(nameof(providerOptionsFactory));
    private readonly Func<IConfigurationAccessor, IProviderQuery> _queryOptionsFactory
        = queryOptionsFactory ?? throw new ArgumentNullException(nameof(queryOptionsFactory));


    public ConfigRule(
        Type providerType,
        IProviderConfiguration providerOptions,
        IProviderQuery queryOptions,
    Type concreteType,
        ConfigRuleOptions? options = null)
        : this(
            providerType,
            _ => providerOptions,
            _ => queryOptions,
            concreteType,
            options)
    {
        if (providerOptions is null)
        {
            throw new ArgumentNullException(nameof(providerOptions));
        }

        if (queryOptions is null)
        {
            throw new ArgumentNullException(nameof(queryOptions));
        }
    }

    public IProviderConfiguration ResolveProviderOptions(IConfigurationAccessor manager)
        => _providerOptionsFactory(manager);

    public IProviderQuery ResolveQueryOptions(IConfigurationAccessor manager)
        => _queryOptionsFactory(manager);

    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(
        TOptions providerOptions,
        TQueryOptions queryOptions,
    Type concreteType,
        ConfigRuleOptions options)
        where TProvider : ConfigurationProvider<TOptions, TQueryOptions>
        where TOptions : IProviderConfiguration
        where TQueryOptions : IProviderQuery =>
        new(typeof(TProvider), providerOptions, queryOptions, concreteType, options);

    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(
        Func<IConfigurationAccessor, TOptions> providerOptionsFactory,
        Func<IConfigurationAccessor, TQueryOptions> queryOptionsFactory,
    Type concreteType,
        ConfigRuleOptions options)
        where TProvider : ConfigurationProvider<TOptions, TQueryOptions>
        where TOptions : IProviderConfiguration
        where TQueryOptions : IProviderQuery
    {
        return new(
            typeof(TProvider),
            m => providerOptionsFactory(m),
            m => queryOptionsFactory(m),
            concreteType,
            options);
    }

}
