namespace Cocoar.Configuration.Flags.Tests;

public class EntitlementsBaseTests
{
    [Fact]
    public void Entitlements_CanBeInherited()
    {
        var entitlements = new TestEntitlements();

        Assert.NotNull(entitlements);
        Assert.IsAssignableFrom<Entitlements>(entitlements);
    }

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var entitlements = new TestEntitlements();

        Assert.NotNull(entitlements);
    }

    [Fact]
    public void Entitlements_HaveNoExpiration()
    {
        var type = typeof(Entitlements);
        var expiresAtProperty = type.GetProperty("ExpiresAt");
        var isExpiredProperty = type.GetProperty("IsExpired");

        Assert.Null(expiresAtProperty);
        Assert.Null(isExpiredProperty);
    }

    [Fact]
    public void Entitlement_CreatesDelegate_ThatReturnsValue()
    {
        var entitlements = new TestEntitlements();

        Assert.True(entitlements.CanExport());
        Assert.Equal(100, entitlements.MaxUsers());
        Assert.Equal("premium", entitlements.Tier());
    }

    [Fact]
    public void Entitlement_WithContext_CreatesDelegate_ThatEvaluatesContext()
    {
        var entitlements = new TestEntitlements();

        Assert.True(entitlements.HasFeature("export"));
        Assert.True(entitlements.HasFeature("reports"));
        Assert.False(entitlements.HasFeature("unknown"));
    }

    #region Test Classes

    private class TestEntitlements : Entitlements
    {
        private readonly List<string> _enabledFeatures = new() { "export", "reports" };

        public Entitlement<bool> CanExport { get; }
        public Entitlement<int> MaxUsers { get; }
        public Entitlement<string> Tier { get; }
        public Entitlement<string, bool> HasFeature { get; }

        public TestEntitlements()
        {
            CanExport = () => true;
            MaxUsers = () => 100;
            Tier = () => "premium";
            HasFeature = feature => _enabledFeatures.Contains(feature);
        }
    }

    #endregion
}
