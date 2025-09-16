using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Builder for creating service registrations with fluent API.
/// </summary>
public class ServiceRegistrationBuilder
{
    private readonly List<IServiceRegistration> _registrations = new();
    private readonly HashSet<(Type ServiceType, object? ServiceKey)> _removals = new();

    /// <summary>
    /// Removes the specified type from auto-registration.
    /// This prevents the type from being automatically registered with the default lifetime.
    /// </summary>
    /// <typeparam name="T">The service type to exclude from auto-registration</typeparam>
    /// <param name="serviceKey">Optional service key to remove</param>
    /// <returns>This builder for method chaining</returns>
    public ServiceRegistrationBuilder Remove<T>(object? serviceKey = null)
        where T : class
    {
        _removals.Add((typeof(T), serviceKey));
        return this;
    }

    /// <summary>
    /// Creates a service registration for the specified type with the given lifetime.
    /// </summary>
    /// <typeparam name="T">The service type to register</typeparam>
    /// <param name="lifetime">The service lifetime</param>
    /// <param name="serviceKey">Optional service key</param>
    /// <returns>This builder for method chaining</returns>
    public ServiceRegistrationBuilder Add<T>(ServiceLifetime lifetime, object? serviceKey = null)
        where T : class
    {
        _registrations.Add(new ServiceRegistration(typeof(T), lifetime, serviceKey));
        return this;
    }

    /// <summary>
    /// Gets the types that should be excluded from auto-registration.
    /// </summary>
    /// <returns>Set of types and service keys to exclude</returns>
    public IReadOnlySet<(Type ServiceType, object? ServiceKey)> GetRemovals()
    {
        return _removals;
    }

    /// <summary>
    /// Builds the service registrations.
    /// </summary>
    /// <returns>Collection of service registrations</returns>
    public IReadOnlyCollection<IServiceRegistration> Build()
    {
        return _registrations.AsReadOnly();
    }
}