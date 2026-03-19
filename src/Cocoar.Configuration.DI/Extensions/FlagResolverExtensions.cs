using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.DI;

/// <summary>
/// DI-layer extension methods on <see cref="ConfigManagerBuilder"/> that add resolver
/// registration to the Core-only <c>UseFeatureFlags</c> and <c>UseEntitlements</c> methods.
/// </summary>
public static class FlagResolverExtensions
{
    /// <summary>
    /// Configures feature flags with context resolver registration for DI.
    /// <para>
    /// The first delegate registers flag classes; the second registers resolvers that hydrate
    /// context for contextual flag evaluation. Resolvers default to <c>Scoped</c> lifetime
    /// and can be customized with <c>.AsSingleton()</c> or <c>.AsTransient()</c>.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCocoarConfiguration(c => c
    ///     .UseConfiguration(rules => [...])
    ///     .UseFeatureFlags(
    ///         flags => [
    ///             flags.Register&lt;BillingFlags&gt;()
    ///         ],
    ///         resolvers => [
    ///             resolvers.Global&lt;UserByIdResolver&gt;(),
    ///             resolvers.For&lt;BillingFlags&gt;(r => r
    ///                 .Use&lt;BillingResolver&gt;()
    ///                 .ForProperty(f => f.BetaCheckout).Use&lt;BetaResolver&gt;())
    ///         ]));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseFeatureFlags(
        this ConfigManagerBuilder builder,
        Func<FlagsBuilder, FlagRegistration[]> flags,
        Func<ResolverBuilder, ResolverRegistration[]> resolvers)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(resolvers);

        var flagsBuilder = new FlagsBuilder();
        var flagRegistrations = flags(flagsBuilder);

        var resolverBuilder = new ResolverBuilder();
        var resolverRegistrations = resolvers(resolverBuilder);

        var (globalResolvers, classResolverMap) = DistributeResolvers(resolverRegistrations);

        // Attach class-specific resolvers to their matching flag registrations
        foreach (var flagReg in flagRegistrations)
        {
            if (classResolverMap.TryGetValue(flagReg.Descriptor.Type, out var classResolvers))
                flagReg.Resolvers = classResolvers;
        }

        ConfigManagerBuilderExtensions.ApplyFeatureFlags(builder, flagRegistrations, globalResolvers);

        // Store resolver registrations on the manager so ServiceDescriptorEmitter can read lifetimes
        var manager = ConfigManagerBuilder.GetManager(builder);
        manager.FlagsSetup!.ResolverRegistrations = resolverRegistrations;

        return builder;
    }

    /// <summary>
    /// Configures entitlements with context resolver registration for DI.
    /// <para>
    /// The first delegate registers entitlement classes; the second registers resolvers that hydrate
    /// context for contextual entitlement evaluation. Resolvers default to <c>Scoped</c> lifetime
    /// and can be customized with <c>.AsSingleton()</c> or <c>.AsTransient()</c>.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCocoarConfiguration(c => c
    ///     .UseConfiguration(rules => [...])
    ///     .UseEntitlements(
    ///         entitlements => [
    ///             entitlements.Register&lt;PlanEntitlements&gt;()
    ///         ],
    ///         resolvers => [
    ///             resolvers.Global&lt;TenantByIdResolver&gt;()
    ///         ]));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseEntitlements(
        this ConfigManagerBuilder builder,
        Func<EntitlementsBuilder, EntitlementRegistration[]> entitlements,
        Func<ResolverBuilder, ResolverRegistration[]> resolvers)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(resolvers);

        var entitlementsBuilder = new EntitlementsBuilder();
        var entitlementRegistrations = entitlements(entitlementsBuilder);

        var resolverBuilder = new ResolverBuilder();
        var resolverRegistrations = resolvers(resolverBuilder);

        var (globalResolvers, classResolverMap) = DistributeResolvers(resolverRegistrations);

        // Attach class-specific resolvers to their matching entitlement registrations
        foreach (var entitlementReg in entitlementRegistrations)
        {
            if (classResolverMap.TryGetValue(entitlementReg.Descriptor.Type, out var classResolvers))
                entitlementReg.Resolvers = classResolvers;
        }

        ConfigManagerBuilderExtensions.ApplyEntitlements(builder, entitlementRegistrations, globalResolvers);

        // Store resolver registrations on the manager so ServiceDescriptorEmitter can read lifetimes
        var manager = ConfigManagerBuilder.GetManager(builder);
        manager.EntitlementsSetup!.ResolverRegistrations = resolverRegistrations;

        return builder;
    }

    /// <summary>
    /// Separates resolver registrations into global resolvers and class-specific resolver maps.
    /// </summary>
    private static (
        IReadOnlyList<ContextResolverRegistration> GlobalResolvers,
        Dictionary<Type, IReadOnlyList<ContextResolverRegistration>> ClassResolverMap)
        DistributeResolvers(ResolverRegistration[] registrations)
    {
        var globalResolvers = new List<ContextResolverRegistration>();
        var classResolverMap = new Dictionary<Type, IReadOnlyList<ContextResolverRegistration>>();

        foreach (var reg in registrations)
        {
            if (reg.FlagClassType is null)
            {
                // Global resolver
                foreach (var r in reg.Registrations)
                    globalResolvers.Add(r);
            }
            else
            {
                // Class-scoped resolvers
                classResolverMap[reg.FlagClassType] = reg.Registrations;
            }
        }

        return (globalResolvers.AsReadOnly(), classResolverMap);
    }

}
