namespace Cocoar.Configuration.Flags;

/// <summary>
/// Compile-time descriptor for a <see cref="FeatureFlags"/> subclass.
/// Populated at startup by the source generator via <c>CocoarFlagsDescriptors.Flags</c>.
/// </summary>
public sealed record FeatureFlagClassDescriptor(
    Type Type,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<FlagDefinitionDescriptor> Flags)
{
    /// <summary>Whether the class-level expiry has passed.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}
