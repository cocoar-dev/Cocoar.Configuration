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
    /// The <paramref name="configure"/> delegate receives a <see cref="FeatureFlagsSetupBuilder"/>
    /// where each flags class is registered explicitly via <c>Register&lt;T&gt;()</c>.
    /// Descriptor metadata (expiry, flag names) is resolved from the source-generated
    /// <c>CocoarFlagsDescriptors</c> dictionary in the caller's assembly.
    /// </para>
    /// <para>
    /// Registers a singleton <see cref="IFeatureFlagsRegistry"/>. Each registered flag class
    /// is added to DI with its specified <see cref="FlagLifetime"/> (default: Scoped).
    /// Expired flag classes will appear as
    /// <see cref="Cocoar.Configuration.Health.HealthStatus.Degraded"/> in the health API.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...], setup => [...])
    ///     .UseFeatureFlags(f => f
    ///         .Register&lt;AppFeatureFlags&gt;()
    ///         .Register&lt;AdminFlags&gt;(FlagLifetime.Singleton)));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseFeatureFlags(
        this ConfigManagerBuilder builder,
        Action<FeatureFlagsSetupBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var setupBuilder = new FeatureFlagsSetupBuilder();
        configure(setupBuilder);
        var registrations = setupBuilder.Build();

        var registry = new FeatureFlagsRegistry();
        foreach (var r in registrations)
            registry.RegisterDescriptor(r.Descriptor);

        var healthSource = new FeatureFlagsHealthSource(registry);
        builder.SetFlagsHealthSource(healthSource);

        var scope = ConfigManagerBuilder.GetCapabilityScope(builder);
        scope.Compose(FlagsCapability.ScopeKey)
            .WithPrimary(new FlagsCapability { Registry = registry, Registrations = registrations })
            .Build();

        return builder;
    }

    /// <summary>
    /// Configures entitlements for DI registration.
    /// <para>
    /// The <paramref name="configure"/> delegate receives an <see cref="EntitlementsSetupBuilder"/>
    /// where each entitlements class is registered explicitly via <c>Register&lt;T&gt;()</c>.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...], setup => [...])
    ///     .UseEntitlements(e => e
    ///         .Register&lt;AppPlanEntitlements&gt;()));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseEntitlements(
        this ConfigManagerBuilder builder,
        Action<EntitlementsSetupBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var setupBuilder = new EntitlementsSetupBuilder();
        configure(setupBuilder);
        var registrations = setupBuilder.Build();

        var registry = new EntitlementsRegistry();
        foreach (var r in registrations)
            registry.RegisterDescriptor(r.Descriptor);

        var scope = ConfigManagerBuilder.GetCapabilityScope(builder);
        scope.Compose(EntitlementsCapability.ScopeKey)
            .WithPrimary(new EntitlementsCapability { Registry = registry, Registrations = registrations })
            .Build();

        return builder;
    }
}
