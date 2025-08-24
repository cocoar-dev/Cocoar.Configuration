using Cocoar.Configuration.Fluent.Providers;
using Cocoar.Configuration.Fluent.ProviderOptions;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Fluent;

public static class Rules
{
    // Instance host to allow extension methods from external libraries:
    // usage: Rules.Using.FromMyProvider(...)
    public readonly struct Dsl { }
    public static Dsl Using => default;

    public static FileRuleBuilder FromFile(Func<ConfigManager, FileSourceRuleOptions> optionsFactory) => new(optionsFactory);
    public static EnvironmentRuleBuilder FromEnvironment(Func<ConfigManager, EnvironmentVariableRuleOptions> optionsFactory) => new(optionsFactory);

    // Generic entry point: compose provider instance and query options directly without bespoke combined RuleOptions.
    public static ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> FromProvider<TProvider, TInstanceOptions, TQueryOptions>(
        Func<ConfigManager, TInstanceOptions> instanceOptions,
        Func<ConfigManager, TQueryOptions> queryOptions)
    where TProvider : ConfigSourceProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : ISourceProviderInstanceOptions
    where TQueryOptions : ISourceProviderQueryOptions
        => new(instanceOptions, queryOptions);
}
