namespace Cocoar.Configuration;

/// <summary>
/// Fluent API for creating type binding specifications.
/// Enables binding concrete types to interfaces for configuration retrieval.
/// </summary>
public static class Bind
{
    /// <summary>
    /// Starts building a binding specification for the given concrete type.
    /// </summary>
    /// <typeparam name="TConcreteType">The concrete type to bind</typeparam>
    /// <returns>A builder to define interface bindings</returns>
    public static BindingBuilder<TConcreteType> Type<TConcreteType>()
        where TConcreteType : class
    {
        return new BindingBuilder<TConcreteType>();
    }
}

/// <summary>
/// Builder for creating binding specifications with fluent interface syntax.
/// </summary>
/// <typeparam name="TConcreteType">The concrete type being bound</typeparam>
public sealed class BindingBuilder<TConcreteType> where TConcreteType : class
{
    private readonly List<Type> _boundInterfaces = new();

    internal BindingBuilder() { }

    /// <summary>
    /// Binds the concrete type to the specified interface.
    /// </summary>
    /// <typeparam name="TInterface">The interface to bind to</typeparam>
    /// <returns>This builder for method chaining</returns>
    public BindingBuilder<TConcreteType> To<TInterface>()
        where TInterface : class
    {
        var interfaceType = typeof(TInterface);
        
        // Runtime validation that TConcreteType implements TInterface
        if (!interfaceType.IsAssignableFrom(typeof(TConcreteType)))
        {
            throw new InvalidOperationException($"Type {typeof(TConcreteType).Name} does not implement interface {interfaceType.Name}");
        }
        
        // Prevent duplicate interface registrations
        if (!_boundInterfaces.Contains(interfaceType))
        {
            _boundInterfaces.Add(interfaceType);
        }
        
        return this;
    }

    /// <summary>
    /// Builds the binding specification.
    /// </summary>
    /// <returns>The completed binding specification</returns>
    public IBindingSpec Build()
    {
        return new BindingSpec(typeof(TConcreteType), _boundInterfaces.AsReadOnly());
    }

    /// <summary>
    /// Implicit conversion to BindingSpec for convenience.
    /// </summary>
    public static implicit operator BindingSpec(BindingBuilder<TConcreteType> builder)
    {
        return (BindingSpec)builder.Build();
    }
}