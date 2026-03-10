namespace Cocoar.Configuration.Flags.Tests;

public class FeatureFlagsRegistryTests : IDisposable
{
    private readonly List<FeatureFlags> _createdFlags = new();

    [Fact]
    public void Register_AddsToRegistry()
    {
        var registry = new FeatureFlagsRegistry();
        var flags = CreateFlags<TestFeatureFlags>();

        registry.Register(flags);

        Assert.Single(registry.GetAll());
        Assert.Contains(flags, registry.GetAll());
    }

    [Fact]
    public void Register_WithNull_ThrowsArgumentNullException()
    {
        var registry = new FeatureFlagsRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void Register_SameTypeTwice_ReplacesInstance()
    {
        var registry = new FeatureFlagsRegistry();
        var flags1 = CreateFlags<TestFeatureFlags>();
        var flags2 = CreateFlags<TestFeatureFlags>();

        registry.Register(flags1);
        registry.Register(flags2);

        Assert.Single(registry.GetAll());
        Assert.Same(flags2, registry.Find<TestFeatureFlags>());
    }

    [Fact]
    public void Unregister_RemovesFromRegistry()
    {
        var registry = new FeatureFlagsRegistry();
        var flags = CreateFlags<TestFeatureFlags>();
        registry.Register(flags);

        var result = registry.Unregister(flags);

        Assert.True(result);
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void Unregister_NotRegistered_ReturnsFalse()
    {
        var registry = new FeatureFlagsRegistry();
        var flags = CreateFlags<TestFeatureFlags>();

        var result = registry.Unregister(flags);

        Assert.False(result);
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var registry = new FeatureFlagsRegistry();
        var flags1 = CreateFlags<TestFeatureFlags>();
        var flags2 = CreateFlags<AnotherFeatureFlags>();

        registry.Register(flags1);
        registry.Register(flags2);

        var all = registry.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(flags1, all);
        Assert.Contains(flags2, all);
    }

    [Fact]
    public void Find_ReturnsTypedInstance()
    {
        var registry = new FeatureFlagsRegistry();
        var flags = CreateFlags<TestFeatureFlags>();
        registry.Register(flags);

        var result = registry.Find<TestFeatureFlags>();

        Assert.Same(flags, result);
    }

    [Fact]
    public void Find_NotRegistered_ReturnsNull()
    {
        var registry = new FeatureFlagsRegistry();

        var result = registry.Find<TestFeatureFlags>();

        Assert.Null(result);
    }

    [Fact]
    public void GetExpired_ReturnsOnlyExpiredFlags()
    {
        var registry = new FeatureFlagsRegistry();
        var expired = CreateFlags<ExpiredFeatureFlags>();
        var valid = CreateFlags<TestFeatureFlags>();

        registry.Register(expired);
        registry.Register(valid);

        var expiredFlags = registry.GetExpired();

        Assert.Single(expiredFlags);
        Assert.Contains(expired, expiredFlags);
        Assert.DoesNotContain(valid, expiredFlags);
    }

    private T CreateFlags<T>() where T : FeatureFlags, new()
    {
        var flags = new T();
        _createdFlags.Add(flags);
        return flags;
    }

    public void Dispose()
    {
        foreach (var flags in _createdFlags)
        {
            flags.Dispose();
        }
    }

    private class TestFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
        public TestFeatureFlags() : base(null) { }
    }

    private class AnotherFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 6, 1, 0, 0, 0, TimeSpan.Zero);
        public AnotherFeatureFlags() : base(null) { }
    }

    private class ExpiredFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public ExpiredFeatureFlags() : base(null) { }
    }
}

public class EntitlementsRegistryTests : IDisposable
{
    private readonly List<Entitlements> _createdEntitlements = new();

    [Fact]
    public void Register_AddsToRegistry()
    {
        var registry = new EntitlementsRegistry();
        var entitlements = CreateEntitlements<TestEntitlements>();

        registry.Register(entitlements);

        Assert.Single(registry.GetAll());
        Assert.Contains(entitlements, registry.GetAll());
    }

    [Fact]
    public void Register_WithNull_ThrowsArgumentNullException()
    {
        var registry = new EntitlementsRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void Register_SameTypeTwice_ReplacesInstance()
    {
        var registry = new EntitlementsRegistry();
        var e1 = CreateEntitlements<TestEntitlements>();
        var e2 = CreateEntitlements<TestEntitlements>();

        registry.Register(e1);
        registry.Register(e2);

        Assert.Single(registry.GetAll());
        Assert.Same(e2, registry.Find<TestEntitlements>());
    }

    [Fact]
    public void Unregister_RemovesFromRegistry()
    {
        var registry = new EntitlementsRegistry();
        var entitlements = CreateEntitlements<TestEntitlements>();
        registry.Register(entitlements);

        var result = registry.Unregister(entitlements);

        Assert.True(result);
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var registry = new EntitlementsRegistry();
        var e1 = CreateEntitlements<TestEntitlements>();
        var e2 = CreateEntitlements<AnotherEntitlements>();

        registry.Register(e1);
        registry.Register(e2);

        var all = registry.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(e1, all);
        Assert.Contains(e2, all);
    }

    [Fact]
    public void Find_ReturnsTypedInstance()
    {
        var registry = new EntitlementsRegistry();
        var entitlements = CreateEntitlements<TestEntitlements>();
        registry.Register(entitlements);

        var result = registry.Find<TestEntitlements>();

        Assert.Same(entitlements, result);
    }

    [Fact]
    public void Find_NotRegistered_ReturnsNull()
    {
        var registry = new EntitlementsRegistry();

        var result = registry.Find<TestEntitlements>();

        Assert.Null(result);
    }

    private T CreateEntitlements<T>() where T : Entitlements, new()
    {
        var entitlements = new T();
        _createdEntitlements.Add(entitlements);
        return entitlements;
    }

    public void Dispose()
    {
        foreach (var entitlements in _createdEntitlements)
        {
            entitlements.Dispose();
        }
    }

    private class TestEntitlements : Entitlements
    {
        public TestEntitlements() : base(null) { }
    }

    private class AnotherEntitlements : Entitlements
    {
        public AnotherEntitlements() : base(null) { }
    }
}
