namespace Cocoar.Configuration.Providers.Abstractions;

/// <summary>
/// Implemented by provider options that need additional services registered in DI
/// beyond the standard config type and <c>IReactiveConfig&lt;T&gt;</c>.
/// <para>
/// The DI emitter discovers this interface by scanning resolved provider options
/// for all rules. No hardcoded provider knowledge is needed in the emitter.
/// </para>
/// </summary>
public interface IProviderServiceRegistration
{
    /// <summary>
    /// Returns additional service registrations (eager instances and/or resolve-time factories) to apply in DI.
    /// Called once during DI setup — not on every recompute.
    /// </summary>
    /// <param name="concreteType">The configuration type this rule targets (e.g., typeof(AppSettings)).</param>
    IEnumerable<ProviderServiceRegistration> GetServiceRegistrations(Type concreteType);
}

/// <summary>
/// A single service registration contributed by a provider. Expresses either a pre-built singleton
/// <see cref="Instance"/> or a resolve-time <see cref="Factory"/>. Uses only BCL types
/// (<see cref="IServiceProvider"/>) so the shipped package stays free of a DI dependency; the DI emitter
/// translates it into a concrete <c>ServiceDescriptor</c>.
/// </summary>
public sealed class ProviderServiceRegistration
{
    private ProviderServiceRegistration(Type serviceType, object? instance, Func<IServiceProvider, object>? factory)
    {
        ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        Instance = instance;
        Factory = factory;
    }

    /// <summary>The service type to register (e.g. <c>ILocalStorage&lt;AppSettings&gt;</c>).</summary>
    public Type ServiceType { get; }

    /// <summary>The pre-built singleton instance, or <see langword="null"/> when a <see cref="Factory"/> is used.</summary>
    public object? Instance { get; }

    /// <summary>The resolve-time factory, or <see langword="null"/> when an <see cref="Instance"/> is used.</summary>
    public Func<IServiceProvider, object>? Factory { get; }

    /// <summary>Registers a pre-built singleton instance.</summary>
    public static ProviderServiceRegistration Singleton(Type serviceType, object instance)
        => new(serviceType, instance ?? throw new ArgumentNullException(nameof(instance)), null);

    /// <summary>Registers a singleton built by a resolve-time factory (so it can pull other DI services).</summary>
    public static ProviderServiceRegistration Singleton(Type serviceType, Func<IServiceProvider, object> factory)
        => new(serviceType, null, factory ?? throw new ArgumentNullException(nameof(factory)));
}
