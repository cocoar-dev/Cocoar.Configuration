using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cocoar.Configuration.Extensions;

// For now, you may need to adjust ConfigRule to store the provider key, contract type, section name:
public record ConfigRule(
    Type ProviderType,
    ISourceProviderInstanceOptions ProviderOptions,
    ISourceProviderQueryOptions QueryOptions,
    Type ConfigContract,
    ConfigLifetime? Lifetime = null)
{
    // public static ConfigRule Create<TProvider,TQueryOptions>(TQueryOptions queryOptions, Type configContract,  ConfigLifetime? lifetime = null)
    //     where TProvider : ConfigSourceProvider
    // {
    //     return new ConfigRule(typeof(TProvider), null, queryOptions, configContract, lifetime);
    // }
    
    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(TOptions providerOptions, TQueryOptions queryOptions, Type configContract, ConfigLifetime? lifetime = null)
        where TProvider : ConfigSourceProvider<TOptions, TQueryOptions>
        where TOptions : ISourceProviderInstanceOptions
    where TQueryOptions: ISourceProviderQueryOptions
    {
        return new ConfigRule(typeof(TProvider), providerOptions, queryOptions, configContract, lifetime);
    }
}

public enum ConfigLifetime
{
    Singleton,
    Scoped,
    Transient
}