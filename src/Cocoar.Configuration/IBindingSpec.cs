namespace Cocoar.Configuration;

public interface IBindingSpec
{
    Type ConcreteType { get; }
    
    IReadOnlyCollection<Type> BoundInterfaces { get; }
}
