namespace Cocoar.Configuration.Flags;

/// <summary>
/// Compile-time descriptor for an <see cref="Entitlements"/> subclass.
/// Populated at startup by the source generator via <c>CocoarFlagsDescriptors.Entitlements</c>.
/// </summary>
public sealed record EntitlementClassDescriptor(
    Type Type,
    IReadOnlyList<EntitlementDefinitionDescriptor> Entitlements);
