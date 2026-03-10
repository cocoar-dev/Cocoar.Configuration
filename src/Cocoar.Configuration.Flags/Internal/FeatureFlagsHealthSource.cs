using Cocoar.Configuration.Health;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Provides expired feature flag data for <see cref="IFlagsHealthSource"/>.
/// Reads from the descriptor registry to return one health entry per
/// expired flags class, driving <see cref="HealthStatus.Degraded"/> in snapshots.
/// </summary>
internal sealed class FeatureFlagsHealthSource(IFeatureFlagsRegistry registry) : IFlagsHealthSource
{
    public IReadOnlyList<FlagClassHealthEntry> GetExpiredFeatureFlags()
    {
        var expired = registry.GetExpiredDescriptors();
        if (expired.Count == 0) return [];

        var entries = new List<FlagClassHealthEntry>(expired.Count);
        foreach (var descriptor in expired)
        {
            entries.Add(new FlagClassHealthEntry(
                TypeName: descriptor.Type.Name,
                ExpiresAt: descriptor.ExpiresAt,
                IsExpired: true,
                TotalFlags: descriptor.Flags.Count,
                ExpiredFlags: descriptor.Flags.Count(f => DateTimeOffset.UtcNow > f.ExpiresAt)));
        }
        return entries;
    }
}
