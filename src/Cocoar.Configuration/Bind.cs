namespace Cocoar.Configuration;


public static class Bind
{
    public static BindingBuilder<TConcreteType> Type<TConcreteType>()
        where TConcreteType : class =>
        new();
}

public sealed class BindingBuilder<TConcreteType> where TConcreteType : class
{
    private readonly List<Type> _boundInterfaces = [];

    internal BindingBuilder() { }

    public BindingBuilder<TConcreteType> To<TInterface>()
        where TInterface : class
    {
        var interfaceType = typeof(TInterface);
        
        if (!interfaceType.IsAssignableFrom(typeof(TConcreteType)))
        {
            throw new InvalidOperationException($"Type {typeof(TConcreteType).Name} does not implement interface {interfaceType.Name}");
        }
        
        if (!_boundInterfaces.Contains(interfaceType))
        {
            _boundInterfaces.Add(interfaceType);
        }
        
        return this;
    }

    public IBindingSpec Build() => new BindingSpec(typeof(TConcreteType), _boundInterfaces.AsReadOnly());


    public static implicit operator BindingSpec(BindingBuilder<TConcreteType> builder) => (BindingSpec)builder.Build();
    
}
