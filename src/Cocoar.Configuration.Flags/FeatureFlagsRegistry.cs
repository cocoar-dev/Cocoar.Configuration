using System.Collections.Concurrent;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Thread-safe implementation of <see cref="IFeatureFlagsRegistry"/>.
/// Register this as a singleton in your DI container.
/// </summary>
public sealed class FeatureFlagsRegistry : IFeatureFlagsRegistry
{
    private readonly ConcurrentDictionary<Type, FeatureFlagClassDescriptor> _registry = new();

    /// <inheritdoc />
    public void RegisterDescriptor(FeatureFlagClassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _registry[descriptor.Type] = descriptor;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<FeatureFlagClassDescriptor> GetDescriptors()
    {
        return _registry.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<FeatureFlagClassDescriptor> GetExpiredDescriptors()
    {
        return _registry.Values.Where(d => d.IsExpired).ToList().AsReadOnly();
    }
}
