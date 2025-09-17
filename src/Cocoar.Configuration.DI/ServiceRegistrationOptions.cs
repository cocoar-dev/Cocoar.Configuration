using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Configuration options for service registrations in the DI container.
/// Provides default registration lifetime and access to explicit registration builder.
/// </summary>
public sealed class ServiceRegistrationOptions
{
    private ServiceLifetime? _defaultLifetime = ServiceLifetime.Scoped;
    private bool _autoRegisterReactiveConfigs = true;
    
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
    /// Gets whether IReactiveConfig&lt;T&gt; should be automatically registered for all configuration types.
    /// Default is true for maximum usability.
    /// </summary>
    public bool AutoRegisterReactiveConfigs => _autoRegisterReactiveConfigs;

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

    /// <summary>
    /// Disables automatic registration of IReactiveConfig&lt;T&gt; for all configuration types.
    /// Use this for advanced scenarios with hundreds of configuration types where you want 
    /// to manually register only the reactive configs you actually need.
    /// Default behavior is to auto-register all reactive configs as Singletons.
    /// </summary>
    /// <returns>This options instance for fluent chaining.</returns>
    public ServiceRegistrationOptions DisableAutoReactiveRegistration()
    {
        _autoRegisterReactiveConfigs = false;
        return this;
    }
}