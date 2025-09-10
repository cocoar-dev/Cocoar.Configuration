namespace Cocoar.Configuration.Fluent;

public abstract class RuleBuilderBase<TBuilder>
    where TBuilder : RuleBuilderBase<TBuilder>
{
    protected bool _required = true;
    protected Func<bool>? _useWhen;
    protected Type? _concreteType;
    protected Type? _interfaceType;

    public TBuilder Required(bool value = true)
    {
        _required = value;
        return (TBuilder)this;
    }

    public TBuilder Optional() => Required(false);

    public TBuilder When(Func<bool> predicate)
    {
        _useWhen = predicate;
        return (TBuilder)this;
    }

    public TBuilder For<TConcrete>()
    {
        _concreteType = typeof(TConcrete);
        return (TBuilder)this;
    }

    public TBuilder As<TInterface>()
    {
        _interfaceType = typeof(TInterface);
        return (TBuilder)this;
    }

    protected ConfigTypeDefinition BuildTypeDefinition()
    {
        if (_concreteType is null)
            throw new InvalidOperationException("Concrete type must be specified via For<T>().");
        return new ConfigTypeDefinition(_concreteType, _interfaceType);
    }
}
