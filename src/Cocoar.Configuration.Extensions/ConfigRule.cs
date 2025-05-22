using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cocoar.Configuration.Extensions;

// For now, you may need to adjust ConfigRule to store the provider key, contract type, section name:
public record ConfigRule(
    Type ProviderType,
    IConfigSourceProviderConfig? ProviderOptions,
    Type ConfigContract,
    string? SectionName = null,
    ConfigLifetime? Lifetime = null)
{
    public static ConfigRule Create<TProvider>(Type configContract, string? sectionName = null, ConfigLifetime? lifetime = null)
        where TProvider : ConfigSourceProvider
    {
        return new ConfigRule(typeof(TProvider), null, configContract, sectionName, lifetime);
    }
    
    public static ConfigRule Create<TProvider, TOptions>(TOptions providerOptions, Type configContract, string? sectionName = null, ConfigLifetime? lifetime = null)
        where TProvider : ConfigSourceProvider<TOptions>
        where TOptions : IConfigSourceProviderConfig
    {
        return new ConfigRule(typeof(TProvider), providerOptions, configContract, sectionName, lifetime);
    }
}

public enum ConfigLifetime
{
    Singleton,
    Scoped,
    Transient
}