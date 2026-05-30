using System.Collections.Concurrent;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Stores all feature flags setup data on the ConfigManager directly.
/// Replaces the Cocoar.Capabilities-based FlagsCapability pattern now that
/// Flags are compiled into the same assembly as Core.
/// </summary>
internal sealed class FlagsSetupData
{
    internal readonly ConcurrentDictionary<Type, object> InstanceCache = new();

    // Per-(tenant, flag-type) singletons: the SAME generated flag class constructed with each tenant's own
    // IReactiveConfig<T> (ADR-005 §7). Distinct from the global InstanceCache so tenants never alias the global.
    internal readonly ConcurrentDictionary<(string Tenant, Type Type), object> TenantInstanceCache = new();

    public required FeatureFlagsDescriptors Descriptors { get; init; }
    public required FlagRegistration[] Registrations { get; init; }
    public required IReadOnlyList<ContextResolverRegistration> GlobalResolvers { get; init; }
    public required IReadOnlyDictionary<string, FlagEvaluationEntry> EvaluationEntries { get; init; }

    /// <summary>
    /// DI-layer resolver registrations. Stored here so ServiceDescriptorEmitter can read
    /// lifetime capabilities. Null when no resolvers are registered (Core-only UseFeatureFlags).
    /// </summary>
    internal object[]? ResolverRegistrations { get; set; }
}
