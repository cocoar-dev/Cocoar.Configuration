namespace Cocoar.Configuration;

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