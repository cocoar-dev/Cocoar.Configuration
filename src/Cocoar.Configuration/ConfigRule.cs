using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration;

public record ConfigRule(
    Type ProviderType,
    ISourceProviderInstanceOptions ProviderOptions,
    ISourceProviderQueryOptions QueryOptions,
    ConfigTypeDefinition ConfigContract,
    ConfigRuleOptions? Options = default
    )
{
    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(TOptions providerOptions, TQueryOptions queryOptions, ConfigTypeDefinition typeDefinition, Func<bool>? useWhen = null)
        where TProvider : ConfigSourceProvider<TOptions, TQueryOptions>
        where TOptions : ISourceProviderInstanceOptions
        where TQueryOptions: ISourceProviderQueryOptions
    {
        var options = new ConfigRuleOptions
        {
            UseWhen = useWhen,
            Required = true // Default to required, can be overridden later
        };
        return new ConfigRule(typeof(TProvider), providerOptions, queryOptions, typeDefinition, options);
    }

}

public class ConfigRuleOptions
{
    public Func<bool>? UseWhen { get; set; }
    public bool Required { get; set; }
}
