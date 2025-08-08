using Cocoar.Configuration.Providers;

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
        // Keep placeholders for ProviderOptions/QueryOptions to satisfy non-null requirements;
        // actual values will be created from factories at runtime.
        var placeholderProviderOpts = providerOptionsFactory(new ConfigManager(Array.Empty<ConfigRule>()));
        var placeholderQueryOpts = queryOptionsFactory(new ConfigManager(Array.Empty<ConfigRule>()));

        return new ConfigRule(
            typeof(TProvider),
            placeholderProviderOpts,
            placeholderQueryOpts,
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
