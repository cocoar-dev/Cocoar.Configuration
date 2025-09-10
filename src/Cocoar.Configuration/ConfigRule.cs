using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration;

public record ConfigRule(
    Type ProviderType,
    ISourceProviderInstanceOptions ProviderOptions,
    ISourceProviderQueryOptions QueryOptions,
    ConfigTypeDefinition ConfigContract,
    ConfigRuleOptions? Options = default,
    // Optional factories to build options based on current ConfigManager state
    Func<ConfigManager, ISourceProviderInstanceOptions>? ProviderOptionsFactory = null,
    Func<ConfigManager, ISourceProviderQueryOptions>? QueryOptionsFactory = null
    )
{
    public ISourceProviderInstanceOptions ResolveProviderOptions(ConfigManager manager)
        => ProviderOptionsFactory?.Invoke(manager) ?? ProviderOptions;

    public ISourceProviderQueryOptions ResolveQueryOptions(ConfigManager manager)
        => QueryOptionsFactory?.Invoke(manager) ?? QueryOptions;

    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(TOptions providerOptions, TQueryOptions queryOptions, ConfigTypeDefinition typeDefinition, Func<bool>? useWhen = null, bool required = true)
        where TProvider : ConfigSourceProvider<TOptions, TQueryOptions>
        where TOptions : ISourceProviderInstanceOptions
        where TQueryOptions: ISourceProviderQueryOptions
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
        ConfigTypeDefinition typeDefinition,
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
        // Store null-forgiven placeholders to avoid premature factory invocation.
        // Actual values are produced from factories at runtime in Resolve* methods.
        return new ConfigRule(
            typeof(TProvider),
            default!,
            default!,
            typeDefinition,
            options,
            m => providerOptionsFactory(m),
            m => queryOptionsFactory(m));
    }

}

public class ConfigRuleOptions
{
    public Func<bool>? UseWhen { get; set; }
    public bool Required { get; set; }
}
