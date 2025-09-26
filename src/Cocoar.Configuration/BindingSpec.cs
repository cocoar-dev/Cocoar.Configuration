namespace Cocoar.Configuration;


public sealed record BindingSpec(Type ConcreteType, IReadOnlyCollection<Type> BoundInterfaces) : IBindingSpec;
