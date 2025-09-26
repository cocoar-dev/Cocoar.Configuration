using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

public class ServiceRegistrationBuilder
{
    private readonly List<IServiceRegistration> _registrations = new();
    private readonly HashSet<(Type ServiceType, object? ServiceKey)> _removals = new();

    public ServiceRegistrationBuilder Remove<T>(object? serviceKey = null)
        where T : class
    {
        _removals.Add((typeof(T), serviceKey));
        return this;
    }

    public ServiceRegistrationBuilder Add<T>(ServiceLifetime lifetime, object? serviceKey = null)
        where T : class
    {
        _registrations.Add(new ServiceRegistration(typeof(T), lifetime, serviceKey));
        return this;
    }

    public IReadOnlySet<(Type ServiceType, object? ServiceKey)> GetRemovals()
    {
        return _removals;
    }

    public IReadOnlyCollection<IServiceRegistration> Build()
    {
        return _registrations.AsReadOnly();
    }
}
