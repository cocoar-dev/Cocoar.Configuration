namespace Cocoar.Configuration.Flags;

/// <summary>
/// Descriptor-based registry for <see cref="Entitlements"/> classes.
/// Populated at startup via <c>UseEntitlements(e =&gt; e.Register&lt;T&gt;())</c>.
/// </summary>
/// <remarks>
/// <para>
/// The registry enables scenarios like:
/// </para>
/// <list type="bullet">
///   <item>Inventory of all entitlements in the application</item>
///   <item>Management UI (ConfigHub) displaying all entitlements</item>
/// </list>
/// </remarks>
public interface IEntitlementsRegistry
{
    /// <summary>
    /// Registers an <see cref="EntitlementClassDescriptor"/> for an entitlements class.
    /// </summary>
    void RegisterDescriptor(EntitlementClassDescriptor descriptor);

    /// <summary>
    /// Gets all registered <see cref="EntitlementClassDescriptor"/> instances.
    /// </summary>
    IReadOnlyCollection<EntitlementClassDescriptor> GetDescriptors();
}
