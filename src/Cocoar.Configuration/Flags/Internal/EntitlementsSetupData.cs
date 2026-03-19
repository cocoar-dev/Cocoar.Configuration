using System.Collections.Concurrent;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Stores all entitlements setup data on the ConfigManager directly.
/// Replaces the Cocoar.Capabilities-based EntitlementsCapability pattern now that
/// Flags are compiled into the same assembly as Core.
/// </summary>
internal sealed class EntitlementsSetupData
{
    internal readonly ConcurrentDictionary<Type, object> InstanceCache = new();

    public required EntitlementsDescriptors Descriptors { get; init; }
    public required EntitlementRegistration[] Registrations { get; init; }
    public required IReadOnlyList<ContextResolverRegistration> GlobalResolvers { get; init; }
    public required IReadOnlyDictionary<string, FlagEvaluationEntry> EvaluationEntries { get; init; }

    /// <summary>
    /// DI-layer resolver registrations. Stored here so ServiceDescriptorEmitter can read
    /// lifetime capabilities. Null when no resolvers are registered (Core-only UseEntitlements).
    /// </summary>
    internal object[]? ResolverRegistrations { get; set; }
}
