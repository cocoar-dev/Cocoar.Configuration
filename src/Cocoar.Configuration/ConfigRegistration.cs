using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration;

public record ConfigRegistration(Type ConcreteType, Type? ContractType = null, ServiceLifetime ServiceLifetime = ServiceLifetime.Singleton, string? ServiceKey = null)
{
    public virtual bool Equals(ConfigRegistration? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return ConcreteType == other.ConcreteType && ContractType == other.ContractType && ServiceLifetime == other.ServiceLifetime && ServiceKey == other.ServiceKey;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ConcreteType, ContractType, ServiceLifetime, ServiceKey);
    }
};
