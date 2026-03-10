namespace Cocoar.Configuration.Flags;

/// <summary>
/// Registry for <see cref="Entitlements"/> class instances.
/// Provides a catalog of all registered entitlement classes in the application.
/// </summary>
/// <remarks>
/// <para>
/// The registry enables scenarios like:
/// </para>
/// <list type="bullet">
///   <item>Inventory of all entitlements in the application</item>
///   <item>Management UI (ConfigHub) displaying all entitlements</item>
///   <item>Documentation generation for available entitlements</item>
/// </list>
/// </remarks>
public interface IEntitlementsRegistry
{
    /// <summary>
    /// Registers an <see cref="Entitlements"/> instance.
    /// </summary>
    /// <param name="entitlements">The entitlements instance to register.</param>
    void Register(Entitlements entitlements);

    /// <summary>
    /// Unregisters an <see cref="Entitlements"/> instance.
    /// </summary>
    /// <param name="entitlements">The entitlements instance to unregister.</param>
    /// <returns>True if the instance was found and removed; otherwise false.</returns>
    bool Unregister(Entitlements entitlements);

    /// <summary>
    /// Gets all registered <see cref="Entitlements"/> instances.
    /// </summary>
    IReadOnlyCollection<Entitlements> GetAll();

    /// <summary>
    /// Finds a registered <see cref="Entitlements"/> instance by type.
    /// </summary>
    /// <typeparam name="T">The type of entitlements to retrieve.</typeparam>
    /// <returns>The registered instance, or null if not found.</returns>
    T? Find<T>() where T : Entitlements;
}
