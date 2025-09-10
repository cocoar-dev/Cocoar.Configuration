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

    // Static provider convenience: seed a type with a factory
    public static ProviderRuleBuilder<
        Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProvider,
        Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderOptions,
        Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderQueryOptions
    > FromStatic<T>(Func<ConfigManager, T> factory, string? wrapperPath = null)
    {
        return new(
            cm => new Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderOptions(System.Text.Json.JsonSerializer.SerializeToElement(factory(cm)!)),
            _ => new Cocoar.Configuration.Providers.StaticJsonProvider.StaticJsonProviderQueryOptions(wrapperPath)
        );
    }
}
