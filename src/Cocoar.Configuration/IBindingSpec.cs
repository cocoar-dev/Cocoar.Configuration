namespace Cocoar.Configuration;

/// <summary>
/// Represents a type binding specification that maps interface types to a concrete implementation type.
/// Used by ConfigManager to enable interface-based configuration retrieval.
/// </summary>
public interface IBindingSpec
{
    /// <summary>
    /// The concrete type that will be instantiated and deserialized from configuration.
    /// </summary>
    Type ConcreteType { get; }
    
    /// <summary>
    /// The interface types that can be used to retrieve this concrete type.
    /// </summary>
    IReadOnlyCollection<Type> BoundInterfaces { get; }
}