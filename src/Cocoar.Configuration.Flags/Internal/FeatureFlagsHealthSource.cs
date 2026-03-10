using Cocoar.Configuration.Health;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Provides expired feature flag data for <see cref="IFlagsHealthSource"/>.
/// Wraps the <see cref="IFeatureFlagsRegistry"/> to return one health entry per
/// expired flags class, driving <see cref="HealthStatus.Degraded"/> in snapshots.
/// </summary>
internal sealed class FeatureFlagsHealthSource(IFeatureFlagsRegistry registry) : IFlagsHealthSource
{
    public IReadOnlyList<FlagClassHealthEntry> GetExpiredFeatureFlags()
    {
        var expired = registry.GetExpired();
        if (expired.Count == 0) return [];

        var entries = new List<FlagClassHealthEntry>(expired.Count);
        foreach (var flags in expired)
        {
            entries.Add(new FlagClassHealthEntry(
                TypeName: flags.GetType().Name,
                ExpiresAt: flags.ExpiresAt,
                IsExpired: flags.IsExpired,
                TotalFlags: flags.GetAllMetadata().Count(),
                ExpiredFlags: flags.GetExpiredFlags().Count()));
        }
        return entries;
    }
}
