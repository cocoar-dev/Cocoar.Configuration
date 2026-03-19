using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Represents a registered feature flag class, returned by
/// <see cref="FlagsBuilder.Register{T}"/>. Carries the descriptor metadata
/// and any resolver registrations attached by DI extension methods.
/// </summary>
public sealed class FlagRegistration
{
    internal FeatureFlagClassDescriptor Descriptor { get; }

    /// <summary>
    /// Resolver registrations attached to this flag class. Set by the DI layer's
    /// resolver builder, not by Core.
    /// </summary>
    internal IReadOnlyList<ContextResolverRegistration> Resolvers { get; set; } = [];

    internal FlagRegistration(FeatureFlagClassDescriptor descriptor)
    {
        Descriptor = descriptor;
    }
}
