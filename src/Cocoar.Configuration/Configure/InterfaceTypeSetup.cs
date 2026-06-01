using Cocoar.Capabilities;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Configure;

/// <summary>
/// Configures how an interface type should be deserialized from configuration sources.
/// When configuration JSON contains an interface-typed property, this mapping tells the deserializer
/// which concrete type to instantiate.
/// </summary>
public sealed class InterfaceTypeSetup<TInterface> : SetupDefinition where TInterface : class
{
    public Guid Id { get; } = Guid.NewGuid();
    
    internal InterfaceTypeSetup(ConfigManagerCapabilityScope capabilityScope) : base(capabilityScope)
    {
        var interfaceType = typeof(TInterface);
        if (!interfaceType.IsInterface)
        {
            throw new InvalidOperationException(
                $"{interfaceType.Name} isn't an interface. Interface<T>() requires T to be an interface type.");
        }

        capabilityScope.Compose(this).WithPrimary(
            new InterfaceTypePrimary<SetupDefinition>(interfaceType));
    }

    /// <summary>
    /// Specifies the concrete type to use when deserializing this interface from configuration sources.
    /// The concrete type must implement the interface.
    /// </summary>
    public InterfaceTypeSetup<TInterface> DeserializeTo<TConcrete>() where TConcrete : class, TInterface
    {
        var concreteType = typeof(TConcrete);
        
        GetComposer(this).Add(new DeserializeToCapability<SetupDefinition>(concreteType));

        return this;
    }

    internal override SetupDefinition Build()
    {
        GetComposer(this).Build();
        return this;
    }
}

internal sealed record InterfaceTypePrimary<T>(Type InterfaceType) : IPrimaryTypeCapability
{
    public Type SelectedType => InterfaceType;
}

internal sealed record DeserializeToCapability<T>(Type ConcreteType);
