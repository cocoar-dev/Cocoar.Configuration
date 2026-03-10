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
            .UseFeatureFlags(f => f.Register<SimpleFeatureFlags>(ServiceLifetime.Singleton)));

        using var sp = services.BuildServiceProvider();

        var registry = sp.GetService<IFeatureFlagsRegistry>();
        Assert.NotNull(registry);

        // Registry itself is always singleton regardless of flag lifetime
        Assert.Same(registry, sp.GetService<IFeatureFlagsRegistry>());
    }

    [Fact]
    public void UseFeatureFlags_RegisteredFlagClass_WithSingletonLifetime_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f.Register<SimpleFeatureFlags>(ServiceLifetime.Singleton)));

        using var sp = services.BuildServiceProvider();

        var flags = sp.GetService<SimpleFeatureFlags>();
        Assert.NotNull(flags);
        Assert.Same(flags, sp.GetService<SimpleFeatureFlags>());
    }

    [Fact]
    public void UseFeatureFlags_RegisteredFlagClass_WithTransientLifetime_IsTransient()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f.Register<SimpleFeatureFlags>(ServiceLifetime.Transient)));

        using var sp = services.BuildServiceProvider();

        var flags1 = sp.GetService<SimpleFeatureFlags>();
        var flags2 = sp.GetService<SimpleFeatureFlags>();
        Assert.NotNull(flags1);
        Assert.NotNull(flags2);
        Assert.NotSame(flags1, flags2);
    }

    [Fact]
    public void UseFeatureFlags_RegisteredFlagClass_DefaultLifetime_IsScoped()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f.Register<SimpleFeatureFlags>()));

        using var sp = services.BuildServiceProvider();

        // Same instance within one scope
        using var scope1 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<SimpleFeatureFlags>();
        var b = scope1.ServiceProvider.GetRequiredService<SimpleFeatureFlags>();
        Assert.Same(a, b);

        // Different instance in a different scope
        using var scope2 = sp.CreateScope();
        var c = scope2.ServiceProvider.GetRequiredService<SimpleFeatureFlags>();
        Assert.NotSame(a, c);
    }

    [Fact]
    public void UseFeatureFlags_FlagClass_DescriptorInRegistryFromStartup()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f.Register<SimpleFeatureFlags>()));

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IFeatureFlagsRegistry>();

        // Descriptor is in the registry from startup — no instance required
        var descriptor = registry.GetDescriptors().FirstOrDefault(d => d.Type == typeof(SimpleFeatureFlags));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void UseFeatureFlags_MultipleFlagClasses_AllRegistered()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f
                .Register<SimpleFeatureFlags>()
                .Register<AnotherFeatureFlags>()));

        using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<SimpleFeatureFlags>());
        Assert.NotNull(scope.ServiceProvider.GetService<AnotherFeatureFlags>());
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
    public void UseEntitlements_RegisteredEntitlementClass_WithSingletonLifetime_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => e.Register<SimpleEntitlements>(ServiceLifetime.Singleton)));

        using var sp = services.BuildServiceProvider();

        var entitlements = sp.GetService<SimpleEntitlements>();
        Assert.NotNull(entitlements);
        Assert.Same(entitlements, sp.GetService<SimpleEntitlements>());
    }

    [Fact]
    public void UseEntitlements_EntitlementClass_DescriptorInRegistryFromStartup()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(e => e.Register<SimpleEntitlements>()));

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IEntitlementsRegistry>();

        var descriptor = registry.GetDescriptors().FirstOrDefault(d => d.Type == typeof(SimpleEntitlements));
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
            .UseFeatureFlags(f => f.Register<SimpleFeatureFlags>(ServiceLifetime.Singleton))
            .UseEntitlements(e => e.Register<SimpleEntitlements>(ServiceLifetime.Singleton)));

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
    public void UseFeatureFlags_WithExpiredDescriptor_RegistryReportsExpired()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(f => f.Register<ExpiredFeatureFlags>()));

        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IFeatureFlagsRegistry>();
        Assert.Single(registry.GetExpiredDescriptors());
        Assert.Equal(typeof(ExpiredFeatureFlags), registry.GetExpiredDescriptors().First().Type);
    }

    // ──────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────

    internal sealed class SimpleFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> Feature { get; }

        public SimpleFeatureFlags()
        {
            Feature = DefineFlag(nameof(Feature), () => true);
        }
    }

    internal sealed class AnotherFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 6, 1, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> Other { get; }

        public AnotherFeatureFlags()
        {
            Other = DefineFlag(nameof(Other), () => false);
        }
    }

    internal sealed class ExpiredFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> OldFeature { get; }

        public ExpiredFeatureFlags()
        {
            OldFeature = DefineFlag(nameof(OldFeature), () => true);
        }
    }

    internal sealed class SimpleEntitlements : Entitlements { }
}
