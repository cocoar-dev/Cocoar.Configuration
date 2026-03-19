namespace Cocoar.Configuration.Flags;

/// <summary>
/// Compile-time descriptor for an individual entitlement defined within an <see cref="Entitlements"/> subclass.
/// </summary>
public sealed record EntitlementDefinitionDescriptor(
    string Name,
    string? Description);
