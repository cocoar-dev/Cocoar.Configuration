using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

public sealed record ServiceRegistration(
    Type ServiceType,
    ServiceLifetime Lifetime,
    object? ServiceKey = null
) : IServiceRegistration
{
    public Type ConcreteType => ServiceType;
}
