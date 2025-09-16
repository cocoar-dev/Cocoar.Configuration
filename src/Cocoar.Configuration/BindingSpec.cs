namespace Cocoar.Configuration;

/// <summary>
/// Implementation of IBindingSpec that defines a concrete type and the interfaces it can be bound to.
/// </summary>
public sealed record BindingSpec(Type ConcreteType, IReadOnlyCollection<Type> BoundInterfaces) : IBindingSpec;