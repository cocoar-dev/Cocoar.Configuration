using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Defines a service registration for dependency injection.
/// </summary>
public interface IServiceRegistration
{
    /// <summary>
    /// The concrete type that will provide the configuration.
    /// </summary>
    Type ConcreteType { get; }

    /// <summary>
    /// The service type to register in DI (usually an interface).
    /// </summary>
    Type ServiceType { get; }

    /// <summary>
    /// The service lifetime for this registration.
    /// </summary>
    ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Optional service key for keyed services.
    /// </summary>
    object? ServiceKey { get; }
}