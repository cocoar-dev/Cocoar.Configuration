using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cocoar.Configuration.Extensions;

// For now, you may need to adjust ConfigRule to store the provider key, contract type, section name:
public record ConfigRule(Type ProviderType, object? ProviderOptions, string ProviderKey, Type ConfigContract, string? SectionName = null, ConfigLifetime? Lifetime = null);
//public record ConfigRule<TProvider>(string ProviderKey, Type ConfigContract, string? SectionName = null, ConfigLifetime? Lifetime = null)
//    : ConfigRule(typeof(TProvider), null, ProviderKey, ConfigContract, SectionName, Lifetime)
//    where TProvider : ConfigSourceProvider;
//public record ConfigRule<TProvider, TOptions>(string ProviderKey, TOptions ProviderOptions, Type ConfigContract, string? SectionName = null, ConfigLifetime? Lifetime = null)
//    : ConfigRule(typeof(TProvider), ProviderOptions, ProviderKey, ConfigContract, SectionName, Lifetime)
//    where TProvider : ConfigSourceProvider<TOptions>
//    where TOptions : class
//{
//    public new TOptions ProviderOptions { get; init; } = ProviderOptions;
//}


public enum ConfigLifetime
{
    Singleton,
    Scoped,
    Transient
}