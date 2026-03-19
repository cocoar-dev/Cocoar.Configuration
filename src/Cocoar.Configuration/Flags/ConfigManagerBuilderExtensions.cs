using System.Reflection;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Extension methods on <see cref="ConfigManagerBuilder"/> for integrating feature flags
/// and entitlements into the configuration lifecycle.
/// </summary>
public static class ConfigManagerBuilderExtensions
{
    /// <summary>
    /// Configures feature flags for DI registration and health monitoring.
    /// <para>
    /// The <paramref name="configure"/> delegate receives a <see cref="FlagsBuilder"/>
    /// and returns a collection of <see cref="FlagRegistration"/> instances via collection expressions.
    /// Descriptor metadata (expiry, flag names, descriptions) is resolved from the source-generated
    /// <c>CocoarFlagsDescriptors</c> dictionary in the caller's assembly.
    /// </para>
    /// <para>
    /// To register context resolvers for evaluating contextual flags, use the DI package's
    /// two-parameter overload that accepts a resolver builder.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...], setup => [...])
    ///     .UseFeatureFlags(flags => [
    ///         flags.Register&lt;AppFeatureFlags&gt;(),
    ///         flags.Register&lt;AdminFlags&gt;()
    ///     ]));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseFeatureFlags(
        this ConfigManagerBuilder builder,
        Func<FlagsBuilder, FlagRegistration[]> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var flagsBuilder = new FlagsBuilder();
        var registrations = configure(flagsBuilder);

        ApplyFeatureFlags(builder, registrations, []);

        return builder;
    }

    /// <summary>
    /// Configures entitlements for DI registration.
    /// <para>
    /// The <paramref name="configure"/> delegate receives an <see cref="EntitlementsBuilder"/>
    /// and returns a collection of <see cref="EntitlementRegistration"/> instances via collection expressions.
    /// </para>
    /// <para>
    /// To register context resolvers for evaluating contextual entitlements, use the DI package's
    /// two-parameter overload that accepts a resolver builder.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...], setup => [...])
    ///     .UseEntitlements(entitlements => [
    ///         entitlements.Register&lt;AppPlanEntitlements&gt;()
    ///     ]));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseEntitlements(
        this ConfigManagerBuilder builder,
        Func<EntitlementsBuilder, EntitlementRegistration[]> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var entitlementsBuilder = new EntitlementsBuilder();
        var registrations = configure(entitlementsBuilder);

        ApplyEntitlements(builder, registrations, []);

        return builder;
    }

    /// <summary>
    /// Applies feature flags registrations to the ConfigManager. Called by both the Core
    /// one-parameter overload and the DI two-parameter overload.
    /// </summary>
    internal static void ApplyFeatureFlags(
        ConfigManagerBuilder builder,
        FlagRegistration[] registrations,
        IReadOnlyList<ContextResolverRegistration> globalResolvers)
    {
        var classDescriptors = registrations.Select(r => r.Descriptor).ToList().AsReadOnly();
        var descriptors = new FeatureFlagsDescriptors(classDescriptors);

        var healthSource = new FeatureFlagsHealthSource(descriptors);
        builder.SetFlagsHealthSource(healthSource);

        // Run the cascade once at startup to build the evaluation entries map.
        // IFeatureFlagEvaluator and the REST endpoint generator both consume this pre-built map.
        var evaluationEntries = BuildEvaluationEntries(registrations, globalResolvers);

        var manager = ConfigManagerBuilder.GetManager(builder);
        manager.FlagsSetup = new FlagsSetupData
        {
            Descriptors = descriptors,
            Registrations = registrations,
            GlobalResolvers = globalResolvers,
            EvaluationEntries = evaluationEntries
        };
    }

    /// <summary>
    /// Applies entitlements registrations to the ConfigManager. Called by both the Core
    /// one-parameter overload and the DI two-parameter overload.
    /// </summary>
    internal static void ApplyEntitlements(
        ConfigManagerBuilder builder,
        EntitlementRegistration[] registrations,
        IReadOnlyList<ContextResolverRegistration> globalResolvers)
    {
        var classDescriptors = registrations.Select(r => r.Descriptor).ToList().AsReadOnly();
        var descriptors = new EntitlementsDescriptors(classDescriptors);

        // Run the cascade once at startup to build the evaluation entries map.
        // IEntitlementEvaluator and the REST endpoint generator both consume this pre-built map.
        var evaluationEntries = BuildEntitlementEvaluationEntries(registrations, globalResolvers);

        var manager = ConfigManagerBuilder.GetManager(builder);
        manager.EntitlementsSetup = new EntitlementsSetupData
        {
            Descriptors = descriptors,
            Registrations = registrations,
            GlobalResolvers = globalResolvers,
            EvaluationEntries = evaluationEntries
        };
    }

    private static IReadOnlyDictionary<string, FlagEvaluationEntry> BuildEvaluationEntries(
        FlagRegistration[] registrations,
        IReadOnlyList<ContextResolverRegistration> globalResolvers)
    {
        return BuildEvaluationEntriesCore(
            registrations.Select(r => (r.Descriptor.Type, r.Resolvers)),
            globalResolvers,
            typeof(FeatureFlag<,>));
    }

    private static IReadOnlyDictionary<string, FlagEvaluationEntry> BuildEntitlementEvaluationEntries(
        EntitlementRegistration[] registrations,
        IReadOnlyList<ContextResolverRegistration> globalResolvers)
    {
        return BuildEvaluationEntriesCore(
            registrations.Select(r => (r.Descriptor.Type, r.Resolvers)),
            globalResolvers,
            typeof(Entitlement<,>));
    }

    /// <summary>
    /// Scans every contextual property (matching <paramref name="contextualTypeDef"/>) on each
    /// registered class and resolves its winning resolver via the three-level cascade
    /// (property -> class -> global). Properties with no resolver are excluded silently.
    /// </summary>
    private static IReadOnlyDictionary<string, FlagEvaluationEntry> BuildEvaluationEntriesCore(
        IEnumerable<(Type ClassType, IReadOnlyList<ContextResolverRegistration> Resolvers)> registrations,
        IReadOnlyList<ContextResolverRegistration> globalResolvers,
        Type contextualTypeDef)
    {
        var entries = new Dictionary<string, FlagEvaluationEntry>(StringComparer.Ordinal);

        foreach (var (classType, resolvers) in registrations)
        {
            foreach (var prop in classType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.PropertyType.IsGenericType) continue;
                if (prop.PropertyType.GetGenericTypeDefinition() != contextualTypeDef) continue;

                var contextType = prop.PropertyType.GetGenericArguments()[0];
                var resolver = FindResolver(resolvers, globalResolvers, prop.Name, contextType);
                if (resolver is null) continue;

                var key = $"{classType.Name}/{prop.Name}";
                if (entries.ContainsKey(key))
                    throw new InvalidOperationException(
                        $"Duplicate evaluation key '{key}'. Two classes share the name '{classType.Name}'. Use distinct class names to avoid collisions.");
                entries[key] = FlagEvaluationEntry.Create(classType, prop, contextType, resolver);
            }
        }

        return entries;
    }

    /// <summary>
    /// Three-level resolver cascade: property-level -> class-level -> global.
    /// First match wins; returns null when no resolver is registered for this context type.
    /// </summary>
    private static ContextResolverRegistration? FindResolver(
        IReadOnlyList<ContextResolverRegistration> classResolvers,
        IReadOnlyList<ContextResolverRegistration> globalResolvers,
        string propertyName,
        Type contextType)
    {
        // 1. Property-level (most specific)
        var match = classResolvers
            .FirstOrDefault(r => r.PropertyName == propertyName && r.ContextType == contextType);
        if (match is not null) return match;

        // 2. Class-level
        match = classResolvers
            .FirstOrDefault(r => r.PropertyName is null && r.ContextType == contextType);
        if (match is not null) return match;

        // 3. Global fallback
        return globalResolvers.FirstOrDefault(r => r.ContextType == contextType);
    }
}
