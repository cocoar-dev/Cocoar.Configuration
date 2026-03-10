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
    /// Registers a singleton <see cref="IFeatureFlagsRegistry"/> and each flag class registered
    /// via <see cref="FeatureFlagsSetupBuilder.Register{T}"/>. Expired flag classes will appear
    /// as <see cref="Cocoar.Configuration.Health.HealthStatus.Degraded"/> in the health API.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...], setup => [...])
    ///     .UseFeatureFlags(flags => flags
    ///         .Register&lt;BillingFeatureFlags&gt;()
    ///         .Register&lt;ShippingFeatureFlags&gt;()));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseFeatureFlags(
        this ConfigManagerBuilder builder,
        Action<FeatureFlagsSetupBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var flagsBuilder = new FeatureFlagsSetupBuilder();
        configure(flagsBuilder);

        var registry = new FeatureFlagsRegistry();
        var healthSource = new FeatureFlagsHealthSource(registry);

        builder.SetFlagsHealthSource(healthSource);

        var scope = ConfigManagerBuilder.GetCapabilityScope(builder);
        scope.Compose(FlagsCapability.ScopeKey)
            .WithPrimary(new FlagsCapability
            {
                Registry = registry,
                Types = flagsBuilder.Types
            })
            .Build();

        return builder;
    }

    /// <summary>
    /// Configures entitlements for DI registration.
    /// <para>
    /// Registers a singleton <see cref="IEntitlementsRegistry"/> and each entitlement class
    /// registered via <see cref="EntitlementsSetupBuilder.Register{T}"/>.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...], setup => [...])
    ///     .UseEntitlements(e => e
    ///         .Register&lt;PlanEntitlements&gt;()));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseEntitlements(
        this ConfigManagerBuilder builder,
        Action<EntitlementsSetupBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var entitlementsBuilder = new EntitlementsSetupBuilder();
        configure(entitlementsBuilder);

        var registry = new EntitlementsRegistry();

        var scope = ConfigManagerBuilder.GetCapabilityScope(builder);
        scope.Compose(EntitlementsCapability.ScopeKey)
            .WithPrimary(new EntitlementsCapability
            {
                Registry = registry,
                Types = entitlementsBuilder.Types
            })
            .Build();

        return builder;
    }
}
