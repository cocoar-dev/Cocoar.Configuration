using Cocoar.Capabilities;

namespace Cocoar.Configuration.Configure;

/// <summary>
/// Configures how a concrete configuration type is registered and optionally exposed via interfaces.
/// All types are registered as Scoped by default.
/// </summary>
public sealed class ConcreteTypeSetup<T> : SetupDefinition where T : class
{
    public Guid Id { get; } = Guid.NewGuid();
    internal ConcreteTypeSetup(CapabilityScope capabilityScope): base(capabilityScope)
    {
       capabilityScope.For(this).WithPrimary(
            new ConcreteTypePrimary<SetupDefinition>(typeof(T)));
    }
    
    /// <summary>
    /// Exposes this configuration type via an interface for dependency injection.
    /// Useful when you want consumers to depend on abstractions rather than concrete types.
    /// Both the concrete type and the interface will be registered as Scoped.
    /// </summary>
    public ConcreteTypeSetup<T> ExposeAs<TInterface>() where TInterface : class
    {
        var interfaceType = typeof(TInterface);
        if (!interfaceType.IsInterface)
        {
            throw new InvalidOperationException(
                $"{interfaceType.Name} isn't an interface. ExposeAs<T>() requires T to be an interface type.");
        }
        if (!interfaceType.IsAssignableFrom(typeof(T)))
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} doesn't implement {interfaceType.Name}. " +
                $"Check your class definition or remove this ExposeAs call.");
        }
        
        GetComposer(this).Add(new ExposeAsCapability<SetupDefinition>(interfaceType));

        return this;
    }

    internal override SetupDefinition Build()
    {
        GetComposer(this).Build();
        return this;
    }
}
