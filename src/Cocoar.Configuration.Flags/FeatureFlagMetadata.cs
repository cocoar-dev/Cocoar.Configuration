using Cocoar.Capabilities;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Metadata for a feature flag, attached via Capabilities.
/// <para>
/// This is the primary capability attached to feature flag delegates,
/// containing information about the flag's name, expiration, and description.
/// </para>
/// </summary>
public sealed record FeatureFlagMetadata : IPrimaryCapability
{
    /// <summary>
    /// The name of the flag (typically the method/property name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When this flag should be removed from code.
    /// This is a cleanup reminder - the flag continues to work after expiration.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Optional description of what this flag does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Is this flag past its expiration date?
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}
