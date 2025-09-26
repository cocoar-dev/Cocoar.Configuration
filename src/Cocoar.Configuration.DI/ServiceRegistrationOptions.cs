using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

public sealed class ServiceRegistrationOptions
{
    private ServiceLifetime? _defaultLifetime = ServiceLifetime.Scoped;
    private bool _autoRegisterReactiveConfigs = true;
    
    public ServiceRegistrationBuilder Register { get; } = new();

    public ServiceLifetime? DefaultLifetime => _defaultLifetime;

    public bool AutoRegisterReactiveConfigs => _autoRegisterReactiveConfigs;

    public ServiceRegistrationOptions DefaultRegistrationLifetime(ServiceLifetime? lifetime)
    {
        _defaultLifetime = lifetime;
        return this;
    }

    public ServiceRegistrationOptions DisableAutoReactiveRegistration()
    {
        _autoRegisterReactiveConfigs = false;
        return this;
    }
}
