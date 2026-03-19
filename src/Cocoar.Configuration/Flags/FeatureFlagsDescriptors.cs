namespace Cocoar.Configuration.Flags;

/// <summary>
/// Immutable catalog of <see cref="FeatureFlagClassDescriptor"/> instances, built once at startup.
/// </summary>
public sealed class FeatureFlagsDescriptors : IFeatureFlagsDescriptors
{
    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagClassDescriptor> All { get; }

    /// <inheritdoc />
    public IReadOnlyList<FeatureFlagClassDescriptor> Expired { get; }

    internal FeatureFlagsDescriptors(IReadOnlyList<FeatureFlagClassDescriptor> descriptors)
    {
        All = descriptors;
        Expired = descriptors.Where(d => d.IsExpired).ToList().AsReadOnly();
    }
}
