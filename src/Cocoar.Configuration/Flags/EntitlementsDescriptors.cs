namespace Cocoar.Configuration.Flags;

/// <summary>
/// Immutable catalog of <see cref="EntitlementClassDescriptor"/> instances, built once at startup.
/// </summary>
public sealed class EntitlementsDescriptors : IEntitlementsDescriptors
{
    /// <inheritdoc />
    public IReadOnlyList<EntitlementClassDescriptor> All { get; }

    internal EntitlementsDescriptors(IReadOnlyList<EntitlementClassDescriptor> descriptors)
    {
        All = descriptors;
    }
}
