namespace Cocoar.Configuration.Flags;

/// <summary>
/// Compile-time descriptor for an individual flag defined within a <see cref="FeatureFlags"/> subclass.
/// <c>ExpiresAt</c> is either the per-flag override or the class-level expiry.
/// </summary>
public sealed record FlagDefinitionDescriptor(
    string Name,
    DateTimeOffset ExpiresAt,
    string? Description)
{
    /// <summary>Whether this individual flag has passed its expiry.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}
