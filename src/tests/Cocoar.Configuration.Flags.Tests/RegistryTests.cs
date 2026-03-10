namespace Cocoar.Configuration.Flags.Tests;

public class FeatureFlagsRegistryTests
{
    private static readonly DateTimeOffset FutureExpiry = new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PastExpiry = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegisterDescriptor_AddsToRegistry()
    {
        var registry = new FeatureFlagsRegistry();
        var descriptor = MakeFlagsDescriptor(typeof(TestFlags), FutureExpiry);

        registry.RegisterDescriptor(descriptor);

        Assert.Single(registry.GetDescriptors());
        Assert.Contains(descriptor, registry.GetDescriptors());
    }

    [Fact]
    public void RegisterDescriptor_WithNull_ThrowsArgumentNullException()
    {
        var registry = new FeatureFlagsRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.RegisterDescriptor(null!));
    }

    [Fact]
    public void RegisterDescriptor_SameTypeTwice_ReplacesDescriptor()
    {
        var registry = new FeatureFlagsRegistry();
        var d1 = MakeFlagsDescriptor(typeof(TestFlags), FutureExpiry);
        var d2 = MakeFlagsDescriptor(typeof(TestFlags), new DateTimeOffset(2099, 6, 1, 0, 0, 0, TimeSpan.Zero));

        registry.RegisterDescriptor(d1);
        registry.RegisterDescriptor(d2);

        Assert.Single(registry.GetDescriptors());
        Assert.Same(d2, registry.GetDescriptors().First());
    }

    [Fact]
    public void GetDescriptors_ReturnsAllRegistered()
    {
        var registry = new FeatureFlagsRegistry();
        var d1 = MakeFlagsDescriptor(typeof(TestFlags), FutureExpiry);
        var d2 = MakeFlagsDescriptor(typeof(AnotherTestFlags), FutureExpiry);

        registry.RegisterDescriptor(d1);
        registry.RegisterDescriptor(d2);

        var all = registry.GetDescriptors();

        Assert.Equal(2, all.Count);
        Assert.Contains(d1, all);
        Assert.Contains(d2, all);
    }

    [Fact]
    public void GetExpiredDescriptors_ReturnsOnlyExpiredDescriptors()
    {
        var registry = new FeatureFlagsRegistry();
        var expired = MakeFlagsDescriptor(typeof(TestFlags), PastExpiry);
        var valid = MakeFlagsDescriptor(typeof(AnotherTestFlags), FutureExpiry);

        registry.RegisterDescriptor(expired);
        registry.RegisterDescriptor(valid);

        var result = registry.GetExpiredDescriptors();

        Assert.Single(result);
        Assert.Contains(expired, result);
        Assert.DoesNotContain(valid, result);
    }

    private static FeatureFlagClassDescriptor MakeFlagsDescriptor(Type type, DateTimeOffset expiresAt)
        => new(type, expiresAt, []);

    private class TestFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
    }

    private class AnotherTestFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 6, 1, 0, 0, 0, TimeSpan.Zero);
    }
}

public class EntitlementsRegistryTests
{
    [Fact]
    public void RegisterDescriptor_AddsToRegistry()
    {
        var registry = new EntitlementsRegistry();
        var descriptor = MakeEntitlementDescriptor(typeof(TestEntitlements));

        registry.RegisterDescriptor(descriptor);

        Assert.Single(registry.GetDescriptors());
        Assert.Contains(descriptor, registry.GetDescriptors());
    }

    [Fact]
    public void RegisterDescriptor_WithNull_ThrowsArgumentNullException()
    {
        var registry = new EntitlementsRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.RegisterDescriptor(null!));
    }

    [Fact]
    public void RegisterDescriptor_SameTypeTwice_ReplacesDescriptor()
    {
        var registry = new EntitlementsRegistry();
        var d1 = MakeEntitlementDescriptor(typeof(TestEntitlements));
        var d2 = MakeEntitlementDescriptor(typeof(TestEntitlements));

        registry.RegisterDescriptor(d1);
        registry.RegisterDescriptor(d2);

        Assert.Single(registry.GetDescriptors());
        Assert.Same(d2, registry.GetDescriptors().First());
    }

    [Fact]
    public void GetDescriptors_ReturnsAllRegistered()
    {
        var registry = new EntitlementsRegistry();
        var d1 = MakeEntitlementDescriptor(typeof(TestEntitlements));
        var d2 = MakeEntitlementDescriptor(typeof(AnotherEntitlements));

        registry.RegisterDescriptor(d1);
        registry.RegisterDescriptor(d2);

        var all = registry.GetDescriptors();

        Assert.Equal(2, all.Count);
        Assert.Contains(d1, all);
        Assert.Contains(d2, all);
    }

    private static EntitlementClassDescriptor MakeEntitlementDescriptor(Type type)
        => new(type, []);

    private class TestEntitlements : Entitlements { }
    private class AnotherEntitlements : Entitlements { }
}
