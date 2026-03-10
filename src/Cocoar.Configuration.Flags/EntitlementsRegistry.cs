using System.Collections.Concurrent;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Thread-safe implementation of <see cref="IEntitlementsRegistry"/>.
/// Register this as a singleton in your DI container.
/// </summary>
public sealed class EntitlementsRegistry : IEntitlementsRegistry
{
    private readonly ConcurrentDictionary<Type, Entitlements> _registry = new();

    /// <inheritdoc />
    public void Register(Entitlements entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        _registry[entitlements.GetType()] = entitlements;
    }

    /// <inheritdoc />
    public bool Unregister(Entitlements entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        return _registry.TryRemove(entitlements.GetType(), out _);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Entitlements> GetAll()
    {
        return _registry.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public T? Find<T>() where T : Entitlements
    {
        return _registry.TryGetValue(typeof(T), out var entitlements) ? (T)entitlements : null;
    }
}
