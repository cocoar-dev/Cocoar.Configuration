using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Rules;

/// <summary>
/// Represents a single configuration rule that fetches data from a provider and binds it to a concrete type.
/// Rules are executed in order, with later rules overwriting earlier ones (last-write-wins).
/// </summary>
public class ConfigRule
{
    public Type ProviderType { get; }
    public Type ConcreteType { get; }
    public ConfigRuleOptions? Options { get; }

    private readonly Func<IConfigurationAccessor, IProviderConfiguration>? _providerOptionsFactory;
    private readonly Func<IConfigurationAccessor, IProviderQuery>? _queryOptionsFactory;

    public ConfigRule(
        Type providerType,
        Func<IConfigurationAccessor, IProviderConfiguration> providerOptionsFactory,
        Func<IConfigurationAccessor, IProviderQuery> queryOptionsFactory,
        Type concreteType,
        ConfigRuleOptions? options = null)
    {
        ProviderType = providerType ?? throw new ArgumentNullException(nameof(providerType));
        _providerOptionsFactory = providerOptionsFactory ?? throw new ArgumentNullException(nameof(providerOptionsFactory));
        _queryOptionsFactory = queryOptionsFactory ?? throw new ArgumentNullException(nameof(queryOptionsFactory));
        ConcreteType = concreteType ?? throw new ArgumentNullException(nameof(concreteType));
        Options = options;
    }

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
        ArgumentNullException.ThrowIfNull(providerOptions);
        ArgumentNullException.ThrowIfNull(queryOptions);
    }

    /// <summary>
    /// Protected constructor for subclasses (e.g. <see cref="AggregateConfigRule"/>)
    /// that don't have a single provider.
    /// </summary>
    protected ConfigRule(Type concreteType, ConfigRuleOptions? options)
    {
        ConcreteType = concreteType ?? throw new ArgumentNullException(nameof(concreteType));
        Options = options;
        ProviderType = typeof(void);
    }

    /// <summary>
    /// Resolves provider options, potentially using earlier configuration state for dynamic rules.
    /// </summary>
    public IProviderConfiguration ResolveProviderOptions(IConfigurationAccessor manager)
        => _providerOptionsFactory!(manager);

    /// <summary>
    /// Resolves query options, potentially using earlier configuration state for dynamic rules.
    /// </summary>
    public IProviderQuery ResolveQueryOptions(IConfigurationAccessor manager)
        => _queryOptionsFactory!(manager);

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
