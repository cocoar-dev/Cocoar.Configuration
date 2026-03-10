using System.Collections.Concurrent;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Thread-safe implementation of <see cref="IEntitlementsRegistry"/>.
/// Register this as a singleton in your DI container.
/// </summary>
public sealed class EntitlementsRegistry : IEntitlementsRegistry
{
    private readonly ConcurrentDictionary<Type, EntitlementClassDescriptor> _registry = new();

    /// <inheritdoc />
    public void RegisterDescriptor(EntitlementClassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _registry[descriptor.Type] = descriptor;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<EntitlementClassDescriptor> GetDescriptors()
    {
        return _registry.Values.ToList().AsReadOnly();
    }
}
