using Cocoar.Configuration.Health;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Provides expired feature flag detection for <see cref="IFlagsHealthSource"/>.
/// Reads from the descriptor catalog to detect expired flags classes.
/// </summary>
internal sealed class FeatureFlagsHealthSource(IFeatureFlagsDescriptors descriptors) : IFlagsHealthSource
{
    public bool HasExpiredFlags() => descriptors.Expired.Count > 0;
}
