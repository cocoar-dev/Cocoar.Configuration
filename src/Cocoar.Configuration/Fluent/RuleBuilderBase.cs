using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Fluent;

public abstract class RuleBuilderBase<TBuilder>
    where TBuilder : RuleBuilderBase<TBuilder>
{
    protected bool _required = true;
    protected Func<bool>? _useWhen;
    protected Type? _concreteType;
    protected readonly List<ConfigRegistration> _registrations = new();
    protected string? _mountPath; // optional relocation path

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

    // Relocate (mount) the fetched subtree under a new colon-separated root path.
    public TBuilder MountAt(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath)) throw new ArgumentException("mountPath cannot be null/empty", nameof(mountPath));
        _mountPath = mountPath.Trim();
        return (TBuilder)this;
    }

    public TBuilder For<TConcrete>()
    {
        return For<TConcrete>(serviceLifetime: null, serviceKey: null);
    }

    public TBuilder For<TConcrete>(ServiceLifetime? serviceLifetime, string? serviceKey = null)
    {
        _concreteType = typeof(TConcrete);
        
        // If serviceLifetime is specified, automatically register the concrete type
        if (serviceLifetime.HasValue)
        {
            var lifetime = serviceLifetime.Value;
            ValidateRegistration(lifetime, serviceKey);
            var registration = new ConfigRegistration(_concreteType!, _concreteType!, lifetime, serviceKey);
            _registrations.Add(registration);
        }
        
        return (TBuilder)this;
    }

    public TBuilder As<TInterface>(ServiceLifetime? serviceLifetime = null, string? serviceKey = null)
    {
        var lifetime = serviceLifetime ?? ServiceLifetime.Singleton;
        ValidateRegistration(lifetime, serviceKey);
        var registration = new ConfigRegistration(_concreteType!, typeof(TInterface), lifetime, serviceKey);
        _registrations.Add(registration);
        return (TBuilder)this;
    }

    private void ValidateRegistration(ServiceLifetime lifetime, string? serviceKey)
    {
        if (_concreteType is null)
            throw new InvalidOperationException("Concrete type must be specified via For<T>() before adding service registrations.");

        // Check if there's already a registration with the same lifetime and key
        var existingRegistration = _registrations.FirstOrDefault(r => 
            r.ServiceLifetime == lifetime && r.ServiceKey == serviceKey);
        
        if (existingRegistration != null)
        {
            var keyDescription = serviceKey is null ? "without a key" : $"with key '{serviceKey}'";
            throw new InvalidOperationException(
                $"A {lifetime} registration {keyDescription} already exists for type {_concreteType.Name}. " +
                "Each lifetime can only be registered once per key.");
        }
    }

    protected IReadOnlyList<ConfigRegistration> BuildTypeDefinitions()
    {
        if (_concreteType is null)
            throw new InvalidOperationException("Concrete type must be specified via For<T>().");
        
        // If no explicit registrations were made, create a default singleton registration
        if (_registrations.Count == 0)
        {
            var defaultRegistration = new ConfigRegistration(_concreteType);
            return new[] { defaultRegistration };
        }
        
        return _registrations.AsReadOnly();
    }
}
