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
