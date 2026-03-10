using System.Collections.Concurrent;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Thread-safe implementation of <see cref="IFeatureFlagsRegistry"/>.
/// Register this as a singleton in your DI container.
/// </summary>
public sealed class FeatureFlagsRegistry : IFeatureFlagsRegistry
{
    private readonly ConcurrentDictionary<Type, FeatureFlags> _registry = new();

    /// <inheritdoc />
    public void Register(FeatureFlags featureFlags)
    {
        ArgumentNullException.ThrowIfNull(featureFlags);
        _registry[featureFlags.GetType()] = featureFlags;
    }

    /// <inheritdoc />
    public bool Unregister(FeatureFlags featureFlags)
    {
        ArgumentNullException.ThrowIfNull(featureFlags);
        return _registry.TryRemove(featureFlags.GetType(), out _);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<FeatureFlags> GetAll()
    {
        return _registry.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public T? Find<T>() where T : FeatureFlags
    {
        return _registry.TryGetValue(typeof(T), out var flags) ? (T)flags : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<FeatureFlags> GetExpired()
    {
        return _registry.Values.Where(f => f.IsExpired).ToList().AsReadOnly();
    }
}
