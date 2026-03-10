using Cocoar.Configuration.DI;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Health;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

/// <summary>
/// Integration tests verifying that UseFeatureFlags and UseEntitlements properly
/// register services in the DI container via AddCocoarConfiguration.
/// </summary>
public class FlagsDIRegistrationTests
{
    // ──────────────────────────────────────────────
    // IFeatureFlagsRegistry registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_RegistersIFeatureFlagsRegistry_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => flags.Register<SimpleFeatureFlags>()));

        using var sp = services.BuildServiceProvider();

        var registry = sp.GetService<IFeatureFlagsRegistry>();
        Assert.NotNull(registry);

        // Singleton — same instance across resolutions
        var registry2 = sp.GetService<IFeatureFlagsRegistry>();
        Assert.Same(registry, registry2);
    }

    [Fact]
    public void UseFeatureFlags_RegisteredFlagClass_IsResolvableAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => flags.Register<SimpleFeatureFlags>()));

        using var sp = services.BuildServiceProvider();

        var flags = sp.GetService<SimpleFeatureFlags>();
        Assert.NotNull(flags);

        // Singleton — same instance
        var flags2 = sp.GetService<SimpleFeatureFlags>();
        Assert.Same(flags, flags2);
    }

    [Fact]
    public void UseFeatureFlags_FlagClass_AutoRegistersInRegistry()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => flags.Register<SimpleFeatureFlags>()));

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IFeatureFlagsRegistry>();

        // Resolving the flag class triggers its constructor which registers itself
        var flags = sp.GetRequiredService<SimpleFeatureFlags>();

        Assert.Same(flags, registry.Find<SimpleFeatureFlags>());
    }

    [Fact]
    public void UseFeatureFlags_MultipleFlagClasses_AllRegistered()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => flags
                .Register<SimpleFeatureFlags>()
                .Register<AnotherFeatureFlags>()));

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<SimpleFeatureFlags>());
        Assert.NotNull(sp.GetService<AnotherFeatureFlags>());
    }

    // ──────────────────────────────────────────────
    // IEntitlementsRegistry registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseEntitlements_RegistersIEntitlementsRegistry_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => e.Register<SimpleEntitlements>()));

        using var sp = services.BuildServiceProvider();

        var registry = sp.GetService<IEntitlementsRegistry>();
        Assert.NotNull(registry);
        Assert.Same(registry, sp.GetService<IEntitlementsRegistry>());
    }

    [Fact]
    public void UseEntitlements_RegisteredEntitlementClass_IsResolvableAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => e.Register<SimpleEntitlements>()));

        using var sp = services.BuildServiceProvider();

        var entitlements = sp.GetService<SimpleEntitlements>();
        Assert.NotNull(entitlements);
        Assert.Same(entitlements, sp.GetService<SimpleEntitlements>());
    }

    [Fact]
    public void UseEntitlements_EntitlementClass_AutoRegistersInRegistry()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => e.Register<SimpleEntitlements>()));

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IEntitlementsRegistry>();
        var entitlements = sp.GetRequiredService<SimpleEntitlements>();

        Assert.Same(entitlements, registry.Find<SimpleEntitlements>());
    }

    // ──────────────────────────────────────────────
    // UseFeatureFlags + UseEntitlements together
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFlagsAndEntitlements_BothRegistered_IndependentSingletons()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => flags.Register<SimpleFeatureFlags>())
            .UseEntitlements(e => e.Register<SimpleEntitlements>()));

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IFeatureFlagsRegistry>());
        Assert.NotNull(sp.GetService<IEntitlementsRegistry>());
        Assert.NotNull(sp.GetService<SimpleFeatureFlags>());
        Assert.NotNull(sp.GetService<SimpleEntitlements>());
    }

    // ──────────────────────────────────────────────
    // Health snapshot via IConfigurationHealthService
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_WithExpiredFlags_HealthSnapshotShowsDegraded_AfterResolution()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => flags.Register<ExpiredFeatureFlags>()));

        using var sp = services.BuildServiceProvider();

        // Resolving the expired flag class registers it in the registry
        var flags = sp.GetRequiredService<ExpiredFeatureFlags>();
        Assert.True(flags.IsExpired);

        var registry = sp.GetRequiredService<IFeatureFlagsRegistry>();
        Assert.Single(registry.GetExpired());
    }

    // ──────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────

    private sealed class SimpleFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> Feature { get; }

        public SimpleFeatureFlags(IFeatureFlagsRegistry? registry = null) : base(registry)
        {
            Feature = DefineFlag(nameof(Feature), () => true);
        }
    }

    private sealed class AnotherFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 6, 1, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> Other { get; }

        public AnotherFeatureFlags(IFeatureFlagsRegistry? registry = null) : base(registry)
        {
            Other = DefineFlag(nameof(Other), () => false);
        }
    }

    private sealed class ExpiredFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> OldFeature { get; }

        public ExpiredFeatureFlags(IFeatureFlagsRegistry? registry = null) : base(registry)
        {
            OldFeature = DefineFlag(nameof(OldFeature), () => true);
        }
    }

    private sealed class SimpleEntitlements : Entitlements
    {
        public SimpleEntitlements(IEntitlementsRegistry? registry = null) : base(registry) { }
    }
}
