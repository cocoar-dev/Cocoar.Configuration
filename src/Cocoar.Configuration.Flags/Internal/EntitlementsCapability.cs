using Cocoar.Capabilities;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Capability stored on the ConfigManager's capability scope by <c>UseEntitlements</c>.
/// Read by the DI emitter to register <see cref="IEntitlementsRegistry"/> and each entitlement
/// class with their specified per-type lifetimes.
/// </summary>
internal sealed class EntitlementsCapability : IPrimaryCapability
{
    /// <summary>Sentinel key used to store this capability on the ConfigManager scope.</summary>
    internal static readonly EntitlementsCapabilityKey ScopeKey = new();

    public required EntitlementsRegistry Registry { get; init; }
    public required IReadOnlyList<EntitlementRegistration> Registrations { get; init; }
}

/// <summary>Unique key type for locating <see cref="EntitlementsCapability"/> on the scope.</summary>
internal sealed class EntitlementsCapabilityKey { }
