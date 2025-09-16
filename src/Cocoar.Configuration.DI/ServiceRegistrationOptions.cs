using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Configuration options for service registrations in the DI container.
/// Provides default registration lifetime and access to explicit registration builder.
/// </summary>
public sealed class ServiceRegistrationOptions
{
    private ServiceLifetime? _defaultLifetime = ServiceLifetime.Scoped;
    
    /// <summary>
    /// Gets the fluent registration builder for explicit service registrations.
    /// These registrations will override any default registrations for the same service type.
    /// </summary>
    public ServiceRegistrationBuilder Register { get; } = new();

    /// <summary>
    /// Gets the default service lifetime that will be used for automatic registrations
    /// of all rule types and binding interfaces not explicitly configured.
    /// Returns null if auto-registration is disabled.
    /// </summary>
    public ServiceLifetime? DefaultLifetime => _defaultLifetime;

    /// <summary>
    /// Sets the default registration lifetime for all rule types and binding interfaces.
    /// Types not explicitly registered via the Register property will use this lifetime.
    /// Pass null to disable auto-registration entirely.
    /// </summary>
    /// <param name="lifetime">The default service lifetime to use, or null to disable auto-registration.</param>
    /// <returns>This options instance for fluent chaining.</returns>
    public ServiceRegistrationOptions DefaultRegistrationLifetime(ServiceLifetime? lifetime)
    {
        _defaultLifetime = lifetime;
        return this;
    }
}