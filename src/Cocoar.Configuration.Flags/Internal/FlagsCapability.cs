using Cocoar.Capabilities;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Capability stored on the ConfigManager's capability scope by <c>UseFeatureFlags</c>.
/// Read by the DI emitter to register <see cref="IFeatureFlagsRegistry"/> and each flag class
/// with their specified per-type lifetimes.
/// </summary>
internal sealed class FlagsCapability : IPrimaryCapability
{
    /// <summary>Sentinel key used to store this capability on the ConfigManager scope.</summary>
    internal static readonly FlagsCapabilityKey ScopeKey = new();

    public required FeatureFlagsRegistry Registry { get; init; }
    public required IReadOnlyList<FlagRegistration> Registrations { get; init; }
}

/// <summary>Unique key type for locating <see cref="FlagsCapability"/> on the scope.</summary>
internal sealed class FlagsCapabilityKey { }
