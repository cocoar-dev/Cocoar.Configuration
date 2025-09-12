using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Fluent;

public abstract class RuleBuilderBase<TBuilder>
    where TBuilder : RuleBuilderBase<TBuilder>
{
    protected bool _required = true;
    protected Func<bool>? _useWhen;
    protected Type? _concreteType;
    protected readonly List<ConfigRegistration> _registrations = new();

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

    public TBuilder AsSingleton<TInterface>()
    {
        ValidateRegistration(ServiceLifetime.Singleton, null);
        var registration = new ConfigRegistration(_concreteType!, typeof(TInterface));
        _registrations.Add(registration);
        return (TBuilder)this;
    }

    public TBuilder AsSingleton<TInterface>(string serviceKey)
    {
        ValidateRegistration(ServiceLifetime.Singleton, serviceKey);
        var registration = new ConfigRegistration(_concreteType!, typeof(TInterface), ServiceLifetime.Singleton, serviceKey);
        _registrations.Add(registration);
        return (TBuilder)this;
    }

    public TBuilder AsScoped<TInterface>()
    {
        ValidateRegistration(ServiceLifetime.Scoped, null);
        var registration = new ConfigRegistration(_concreteType!, typeof(TInterface), ServiceLifetime.Scoped);
        _registrations.Add(registration);
        return (TBuilder)this;
    }

    public TBuilder AsScoped<TInterface>(string serviceKey)
    {
        ValidateRegistration(ServiceLifetime.Scoped, serviceKey);
        var registration = new ConfigRegistration(_concreteType!, typeof(TInterface), ServiceLifetime.Scoped, serviceKey);
        _registrations.Add(registration);
        return (TBuilder)this;
    }

    public TBuilder AsTransient<TInterface>()
    {
        ValidateRegistration(ServiceLifetime.Transient, null);
        var registration = new ConfigRegistration(_concreteType!, typeof(TInterface), ServiceLifetime.Transient);
        _registrations.Add(registration);
        return (TBuilder)this;
    }

    public TBuilder AsTransient<TInterface>(string serviceKey)
    {
        ValidateRegistration(ServiceLifetime.Transient, serviceKey);
        var registration = new ConfigRegistration(_concreteType!, typeof(TInterface), ServiceLifetime.Transient, serviceKey);
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
