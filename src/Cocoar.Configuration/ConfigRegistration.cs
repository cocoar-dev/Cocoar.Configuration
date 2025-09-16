namespace Cocoar.Configuration;

// Represents a unique configuration concrete type within the repository.
public sealed record ConfigRegistration(Type ConcreteType)
{
    public override int GetHashCode() => ConcreteType.GetHashCode();
}
