namespace Cocoar.Configuration.Flags.Tests;

public class FeatureFlagsDescriptorsTests
{
    private static readonly DateTimeOffset FutureExpiry = new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PastExpiry = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void All_ReturnsAllDescriptors()
    {
        var d1 = MakeFlagsDescriptor(typeof(TestFlags), FutureExpiry);
        var d2 = MakeFlagsDescriptor(typeof(AnotherTestFlags), FutureExpiry);

        var descriptors = new FeatureFlagsDescriptors([d1, d2]);

        Assert.Equal(2, descriptors.All.Count);
        Assert.Contains(d1, descriptors.All);
        Assert.Contains(d2, descriptors.All);
    }

    [Fact]
    public void Expired_ReturnsOnlyExpiredDescriptors()
    {
        var expired = MakeFlagsDescriptor(typeof(TestFlags), PastExpiry);
        var valid = MakeFlagsDescriptor(typeof(AnotherTestFlags), FutureExpiry);

        var descriptors = new FeatureFlagsDescriptors([expired, valid]);

        Assert.Single(descriptors.Expired);
        Assert.Contains(expired, descriptors.Expired);
        Assert.DoesNotContain(valid, descriptors.Expired);
    }

    [Fact]
    public void Expired_WhenNoneExpired_ReturnsEmpty()
    {
        var d1 = MakeFlagsDescriptor(typeof(TestFlags), FutureExpiry);
        var d2 = MakeFlagsDescriptor(typeof(AnotherTestFlags), FutureExpiry);

        var descriptors = new FeatureFlagsDescriptors([d1, d2]);

        Assert.Empty(descriptors.Expired);
    }

    [Fact]
    public void All_WhenEmpty_ReturnsEmpty()
    {
        var descriptors = new FeatureFlagsDescriptors([]);

        Assert.Empty(descriptors.All);
        Assert.Empty(descriptors.Expired);
    }

    private static FeatureFlagClassDescriptor MakeFlagsDescriptor(Type type, DateTimeOffset expiresAt)
        => new(type, expiresAt, []);

    private class TestFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
    }

    private class AnotherTestFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 6, 1, 0, 0, 0, TimeSpan.Zero);
    }
}

public class EntitlementsDescriptorsTests
{
    [Fact]
    public void All_ReturnsAllDescriptors()
    {
        var d1 = MakeEntitlementDescriptor(typeof(TestEntitlements));
        var d2 = MakeEntitlementDescriptor(typeof(AnotherEntitlements));

        var descriptors = new EntitlementsDescriptors([d1, d2]);

        Assert.Equal(2, descriptors.All.Count);
        Assert.Contains(d1, descriptors.All);
        Assert.Contains(d2, descriptors.All);
    }

    [Fact]
    public void All_WhenEmpty_ReturnsEmpty()
    {
        var descriptors = new EntitlementsDescriptors([]);

        Assert.Empty(descriptors.All);
    }

    private static EntitlementClassDescriptor MakeEntitlementDescriptor(Type type)
        => new(type, []);

    private class TestEntitlements { }
    private class AnotherEntitlements { }
}
