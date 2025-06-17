namespace Cocoar.Configuration.Extensions;

public record ConfigRule(
    Type ProviderType,
    ISourceProviderInstanceOptions ProviderOptions,
    ISourceProviderQueryOptions QueryOptions,
    ConfigTypeDefinition ConfigContract,
    ConfigLifetime? Lifetime = null)
{
    public static ConfigRule Create<TProvider, TOptions, TQueryOptions>(TOptions providerOptions, TQueryOptions queryOptions, ConfigTypeDefinition typeDefinition, ConfigLifetime? lifetime = null)
        where TProvider : Providers.ConfigSourceProvider<TOptions, TQueryOptions>
        where TOptions : ISourceProviderInstanceOptions
        where TQueryOptions: ISourceProviderQueryOptions
    {
        return new ConfigRule(typeof(TProvider), providerOptions, queryOptions, typeDefinition, lifetime);
    }
}

public enum ConfigLifetime
{
    Singleton,
    Scoped,
    Transient
}


public record ConfigTypeDefinition(Type ConfigType, Type? ImplementationType = null)
{
    public virtual bool Equals(ConfigTypeDefinition? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return ConfigType == other.ConfigType;
    }

    public override int GetHashCode()
    {
        return ConfigType.GetHashCode();
    }
};