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
    /// Returns additional (serviceType, singletonInstance) pairs to register in DI.
    /// Called once during DI setup — not on every recompute.
    /// </summary>
    /// <param name="concreteType">The configuration type this rule targets (e.g., typeof(AppSettings)).</param>
    IEnumerable<(Type ServiceType, object Implementation)> GetServiceRegistrations(Type concreteType);
}
