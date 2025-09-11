using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Fluent;

public static class Rule
{
    // Instance host to allow extension methods from external libraries:
    // usage: Rule.From.Environment(...), Rule.From.HttpPolling(...)
    public readonly struct Dsl { }
    public static Dsl From => default;

    // Generic entry point: compose provider instance and query options directly without bespoke combined RuleOptions.
    public static ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> FromProvider<TProvider, TInstanceOptions, TQueryOptions>(
        Func<ConfigManager, TInstanceOptions> instanceOptions,
        Func<ConfigManager, TQueryOptions> queryOptions)
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
        => new(instanceOptions, queryOptions);
}
