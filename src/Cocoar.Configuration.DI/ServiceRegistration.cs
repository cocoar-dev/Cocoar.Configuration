using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Implementation of IServiceRegistration that defines how a configuration type should be registered in DI.
/// </summary>
public sealed record ServiceRegistration(
    Type ServiceType,
    ServiceLifetime Lifetime,
    object? ServiceKey = null
) : IServiceRegistration
{
    /// <summary>
    /// The concrete type is the same as the service type for simplified API.
    /// Configuration resolution happens through the binding registry.
    /// </summary>
    public Type ConcreteType => ServiceType;
}