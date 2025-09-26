namespace Cocoar.Configuration.Fluent;

public abstract class RuleBuilderBase<TBuilder>
    where TBuilder : RuleBuilderBase<TBuilder>
{
    protected bool IsRequired;
    protected Func<bool>? UseWhen;
    protected Type? ConcreteType;
    protected string? MountPath;
    protected string? SelectPath;

    public TBuilder Required(bool value = true)
    {
        IsRequired = value;
        return (TBuilder)this;
    }
    
    public TBuilder When(Func<bool> predicate)
    {
        UseWhen = predicate;
        return (TBuilder)this;
    }

    public TBuilder MountAt(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
        {
            throw new ArgumentException("mountPath cannot be null/empty", nameof(mountPath));
        }

        MountPath = mountPath.Trim();
        return (TBuilder)this;
    }

    public TBuilder Select(string selectPath)
    {
        if (string.IsNullOrWhiteSpace(selectPath))
        {
            throw new ArgumentException("selectPath cannot be null/empty", nameof(selectPath));
        }

        SelectPath = selectPath.Trim();
        return (TBuilder)this;
    }

    public TBuilder For<TConcrete>()
    {
        ConcreteType = typeof(TConcrete);
        return (TBuilder)this;
    }

    protected Type BuildRegistration()
    {
        if (ConcreteType is null)
        {
            throw new InvalidOperationException("Concrete type must be specified via For<TConcrete>().");
        }

        return ConcreteType;
    }
}
