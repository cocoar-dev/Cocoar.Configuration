using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Represents a registered entitlement class, returned by
/// <see cref="EntitlementsBuilder.Register{T}"/>. Carries the descriptor metadata
/// and any resolver registrations attached by DI extension methods.
/// </summary>
public sealed class EntitlementRegistration
{
    internal EntitlementClassDescriptor Descriptor { get; }

    /// <summary>
    /// Resolver registrations attached to this entitlement class. Set by the DI layer's
    /// resolver builder, not by Core.
    /// </summary>
    internal IReadOnlyList<ContextResolverRegistration> Resolvers { get; set; } = [];

    internal EntitlementRegistration(EntitlementClassDescriptor descriptor)
    {
        Descriptor = descriptor;
    }
}
