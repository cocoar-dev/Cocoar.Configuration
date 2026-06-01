namespace Cocoar.Configuration.Health;

/// <summary>
/// Provides expired feature flag detection for health tracking.
/// Implemented by <c>FeatureFlagsHealthSource</c> and composed via <c>UseFeatureFlags</c>.
/// </summary>
public interface IFlagsHealthSource
{
    /// <summary>
    /// Returns true if any registered feature flag class has expired.
    /// </summary>
    bool HasExpiredFlags();
}
