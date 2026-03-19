namespace Cocoar.Configuration.Flags;

/// <summary>
/// Compile-time descriptor for an entitlement class.
/// Populated at startup by the source generator via <c>CocoarFlagsDescriptors.Entitlements</c>.
/// </summary>
public sealed record EntitlementClassDescriptor(
    Type Type,
    IReadOnlyList<EntitlementDefinitionDescriptor> Entitlements);
