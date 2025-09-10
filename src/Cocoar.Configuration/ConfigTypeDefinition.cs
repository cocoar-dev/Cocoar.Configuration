namespace Cocoar.Configuration;

public record ConfigRegistration(Type ConcreteType, Type? ContractType = null)
{
    public virtual bool Equals(ConfigRegistration? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return ConcreteType == other.ConcreteType;
    }

    public override int GetHashCode()
    {
        return ConcreteType.GetHashCode();
    }
};