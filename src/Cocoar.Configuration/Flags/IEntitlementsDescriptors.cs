namespace Cocoar.Configuration.Flags;

/// <summary>
/// Read-only catalog of registered <see cref="Entitlements"/> classes.
/// Populated at startup from source-generator output — no runtime reflection required.
/// </summary>
/// <remarks>
/// <para>
/// Use this to:
/// </para>
/// <list type="bullet">
///   <item>Inventory all entitlements in the application</item>
///   <item>Populate management UI (ConfigHub) with entitlement names and descriptions</item>
/// </list>
/// </remarks>
public interface IEntitlementsDescriptors
{
    /// <summary>All registered entitlement class descriptors.</summary>
    IReadOnlyList<EntitlementClassDescriptor> All { get; }
}
