namespace Cocoar.Configuration.Health;

/// <summary>
/// Summary health entry for a single feature flags class.
/// Included in <see cref="ConfigHealthSnapshot.ExpiredFeatureFlags"/> when flags have passed their expiration date.
/// </summary>
/// <param name="TypeName">The simple type name of the feature flags class.</param>
/// <param name="ExpiresAt">When this flags class was scheduled for removal.</param>
/// <param name="IsExpired">Whether the class-level expiry has passed.</param>
/// <param name="TotalFlags">Total number of individual flags defined in the class.</param>
/// <param name="ExpiredFlags">Number of individual flags past their per-flag expiry.</param>
public sealed record FlagClassHealthEntry(
    string TypeName,
    DateTimeOffset ExpiresAt,
    bool IsExpired,
    int TotalFlags,
    int ExpiredFlags);

/// <summary>
/// Provides expired feature flag data for inclusion in health snapshots.
/// Implemented by <c>FeatureFlagsHealthSource</c> in <c>Cocoar.Configuration.Flags</c>
/// and composed onto the capability scope by <c>UseFeatureFlags</c>.
/// </summary>
public interface IFlagsHealthSource
{
    /// <summary>
    /// Returns one entry per registered feature flags class that has expired.
    /// </summary>
    IReadOnlyList<FlagClassHealthEntry> GetExpiredFeatureFlags();
}
