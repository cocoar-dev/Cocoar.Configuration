namespace Cocoar.Configuration.Fluent;

public abstract class RuleBuilderBase<TBuilder>
    where TBuilder : RuleBuilderBase<TBuilder>
{
    protected bool _required = false;
    protected Func<bool>? _useWhen;
    protected Type? _concreteType;
    protected string? _mountPath; // optional relocation path
    protected string? _selectPath; // optional selection path (colon-delimited)

    public TBuilder Required(bool value = true)
    {
        _required = value;
        return (TBuilder)this;
    }
    
    public TBuilder When(Func<bool> predicate)
    {
        _useWhen = predicate;
        return (TBuilder)this;
    }

    // Relocate (mount) the fetched subtree under a new colon-separated root path.
    public TBuilder MountAt(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath)) throw new ArgumentException("mountPath cannot be null/empty", nameof(mountPath));
        _mountPath = mountPath.Trim();
        return (TBuilder)this;
    }

    // Select a nested subsection from the fetched JSON before mounting.
    public TBuilder Select(string selectPath)
    {
        if (string.IsNullOrWhiteSpace(selectPath)) throw new ArgumentException("selectPath cannot be null/empty", nameof(selectPath));
        _selectPath = selectPath.Trim();
        return (TBuilder)this;
    }

    public TBuilder For<TConcrete>()
    {
        _concreteType = typeof(TConcrete);
        return (TBuilder)this;
    }

    protected Type BuildRegistration()
    {
        if (_concreteType is null)
            throw new InvalidOperationException("Concrete type must be specified via For<TConcrete>().");
        return _concreteType;
    }
}
