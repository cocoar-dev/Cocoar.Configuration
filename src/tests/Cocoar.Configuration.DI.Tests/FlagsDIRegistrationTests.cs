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
    // IFeatureFlagsDescriptors registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_RegistersIFeatureFlagsDescriptors_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<SimpleFeatureFlags>()]));

        using var sp = services.BuildServiceProvider();

        var descriptors = sp.GetService<IFeatureFlagsDescriptors>();
        Assert.NotNull(descriptors);

        // Descriptor catalog is always singleton
        Assert.Same(descriptors, sp.GetService<IFeatureFlagsDescriptors>());
    }

    [Fact]
    public void UseFeatureFlags_RegisteredFlagClass_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<SimpleFeatureFlags>()]));

        using var sp = services.BuildServiceProvider();

        var flags = sp.GetService<SimpleFeatureFlags>();
        Assert.NotNull(flags);
        Assert.Same(flags, sp.GetService<SimpleFeatureFlags>());
    }

    [Fact]
    public void UseFeatureFlags_RegisteredFlagClass_IsSingleton_AcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<SimpleFeatureFlags>()]));

        using var sp = services.BuildServiceProvider();

        // FeatureFlag classes are pure functions over reactive config — Singleton is the correct lifetime
        using var scope1 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<SimpleFeatureFlags>();

        using var scope2 = sp.CreateScope();
        var b = scope2.ServiceProvider.GetRequiredService<SimpleFeatureFlags>();

        Assert.Same(a, b); // same instance across all scopes
    }

    [Fact]
    public void UseFeatureFlags_FlagClass_DescriptorInCatalogFromStartup()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<SimpleFeatureFlags>()]));

        using var sp = services.BuildServiceProvider();
        var descriptors = sp.GetRequiredService<IFeatureFlagsDescriptors>();

        // Descriptor is in the catalog from startup — no instance required
        var descriptor = descriptors.All.FirstOrDefault(d => d.Type == typeof(SimpleFeatureFlags));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void UseFeatureFlags_MultipleFlagClasses_AllRegistered()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [
                flags.Register<SimpleFeatureFlags>(),
                flags.Register<AnotherFeatureFlags>()
            ]));

        using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<SimpleFeatureFlags>());
        Assert.NotNull(scope.ServiceProvider.GetService<AnotherFeatureFlags>());
    }

    // ──────────────────────────────────────────────
    // IEntitlementsDescriptors registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseEntitlements_RegistersIEntitlementsDescriptors_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => [e.Register<SimpleEntitlements>()]));

        using var sp = services.BuildServiceProvider();

        var descriptors = sp.GetService<IEntitlementsDescriptors>();
        Assert.NotNull(descriptors);
        Assert.Same(descriptors, sp.GetService<IEntitlementsDescriptors>());
    }

    [Fact]
    public void UseEntitlements_RegisteredEntitlementClass_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => [e.Register<SimpleEntitlements>()]));

        using var sp = services.BuildServiceProvider();

        var entitlements = sp.GetService<SimpleEntitlements>();
        Assert.NotNull(entitlements);
        Assert.Same(entitlements, sp.GetService<SimpleEntitlements>());
    }

    [Fact]
    public void UseEntitlements_EntitlementClass_DescriptorInCatalogFromStartup()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => [e.Register<SimpleEntitlements>()]));

        using var sp = services.BuildServiceProvider();
        var descriptors = sp.GetRequiredService<IEntitlementsDescriptors>();

        var descriptor = descriptors.All.FirstOrDefault(d => d.Type == typeof(SimpleEntitlements));
        Assert.NotNull(descriptor);
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
            .UseFeatureFlags(flags => [flags.Register<SimpleFeatureFlags>()])
            .UseEntitlements(e => [e.Register<SimpleEntitlements>()]));

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IFeatureFlagsDescriptors>());
        Assert.NotNull(sp.GetService<IEntitlementsDescriptors>());
        Assert.NotNull(sp.GetService<SimpleFeatureFlags>());
        Assert.NotNull(sp.GetService<SimpleEntitlements>());
    }

    // ──────────────────────────────────────────────
    // Expired descriptors in catalog
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_WithExpiredDescriptor_CatalogReportsExpired()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<ExpiredFeatureFlags>()]));

        using var sp = services.BuildServiceProvider();

        var descriptors = sp.GetRequiredService<IFeatureFlagsDescriptors>();
        Assert.Single(descriptors.Expired);
        Assert.Equal(typeof(ExpiredFeatureFlags), descriptors.Expired.First().Type);
    }

    // ──────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────

    internal sealed class SimpleFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> Feature { get; }

        public SimpleFeatureFlags()
        {
            Feature = () => true;
        }
    }

    internal sealed class AnotherFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 6, 1, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> Other { get; }

        public AnotherFeatureFlags()
        {
            Other = () => false;
        }
    }

    internal sealed class ExpiredFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> OldFeature { get; }

        public ExpiredFeatureFlags()
        {
            OldFeature = () => true;
        }
    }

    internal sealed class SimpleEntitlements : Entitlements { }
}
