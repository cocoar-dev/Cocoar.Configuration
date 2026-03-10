using Cocoar.Capabilities;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Metadata for an entitlement, attached via Capabilities.
/// <para>
/// This is the primary capability attached to entitlement delegates,
/// containing information about the entitlement's name and description.
/// </para>
/// <para>
/// Unlike <see cref="FeatureFlagMetadata"/>, entitlements do not have an expiration
/// because they represent permanent business rules.
/// </para>
/// </summary>
public sealed record EntitlementMetadata : IPrimaryCapability
{
    /// <summary>
    /// The name of the entitlement (typically the method/property name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description of what this entitlement controls.
    /// </summary>
    public string? Description { get; init; }
}
