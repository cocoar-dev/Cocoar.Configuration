using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Fluent;

public static class Rule
{
    public readonly struct Dsl { }
    public static Dsl From => default;

    public static ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> FromProvider<TProvider, TInstanceOptions, TQueryOptions>(
        Func<IConfigurationAccessor, TInstanceOptions> instanceOptions,
        Func<IConfigurationAccessor, TQueryOptions> queryOptions)
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
        => new(instanceOptions, queryOptions);
}
