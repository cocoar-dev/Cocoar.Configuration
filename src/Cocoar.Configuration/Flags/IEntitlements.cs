namespace Cocoar.Configuration.Flags;

/// <summary>
/// Interface for entitlement classes with typed configuration.
/// Implement this on a partial class — the source generator produces the constructor and Config property.
/// Entitlements are permanent and have no expiration.
/// </summary>
/// <typeparam name="TConfig">
/// The configuration type this entitlement class reads from, or a value tuple of types to read from several at
/// once. Unconstrained on purpose: the generator maps this straight onto <c>IReactiveConfig&lt;TConfig&gt;</c>,
/// which is equally unconstrained, and a <c>class</c> constraint would reject the documented tuple form.
/// </typeparam>
public interface IEntitlements<TConfig>
{
}
