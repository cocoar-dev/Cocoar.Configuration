using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;

namespace Cocoar.Configuration.Fluent;

public static class Rules
{
    // Instance host to allow extension methods from external libraries:
    // usage: Rules.Using.FromMyProvider(...)
    public readonly struct Dsl { }
    public static Dsl Using => default;

    // Generic entry point: compose provider instance and query options directly without bespoke combined RuleOptions.
    public static ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> FromProvider<TProvider, TInstanceOptions, TQueryOptions>(
        Func<ConfigManager, TInstanceOptions> instanceOptions,
        Func<ConfigManager, TQueryOptions> queryOptions)
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
        => new(instanceOptions, queryOptions);
}

public static class RulesExtensions
{
    // Static provider convenience: seed a type with a factory
    public static ProviderRuleBuilder<
        Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProvider,
        Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderOptions,
        Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderQueryOptions
    > FromStatic<T>(this Rules.Dsl dsl, Func<ConfigManager, T> factory, string? TargetPath = null)
    {
        return new(
            cm => new Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderOptions(System.Text.Json.JsonSerializer.SerializeToElement(factory(cm)!)),
            _ => new Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderQueryOptions(TargetPath)
        );
    }
}
