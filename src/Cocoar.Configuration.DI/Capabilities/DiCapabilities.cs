using Cocoar.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI.Capabilities;

/// <summary>
/// Capability that specifies the service lifetime for DI registration.
/// </summary>
public record ServiceLifetimeCapability<T>(ServiceLifetime Lifetime, object? Key);

/// <summary>
/// Capability that prevents automatic DI registration of the concrete type.
/// </summary>
public record DisableAutoRegistrationCapability<T>;

