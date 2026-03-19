namespace Cocoar.Configuration.Flags;

/// <summary>
/// Interface for entitlement classes with typed configuration.
/// Implement this on a partial class — the source generator produces the constructor and Config property.
/// Entitlements are permanent and have no expiration.
/// </summary>
/// <typeparam name="TConfig">The configuration type (or value tuple of types) this entitlement class reads from.</typeparam>
public interface IEntitlements<TConfig> where TConfig : class
{
}
