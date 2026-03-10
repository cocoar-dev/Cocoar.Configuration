namespace Cocoar.Configuration.Flags.Tests;

public class EntitlementsBaseTests
{
    [Fact]
    public void Entitlements_CanBeInherited()
    {
        using var entitlements = new TestEntitlements();

        Assert.NotNull(entitlements);
        Assert.IsAssignableFrom<Entitlements>(entitlements);
    }

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        using var entitlements = new TestEntitlements();

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
        using var entitlements = new TestEntitlements();

        Assert.True(entitlements.CanExport());
        Assert.Equal(100, entitlements.MaxUsers());
        Assert.Equal("premium", entitlements.Tier());
    }

    [Fact]
    public void Entitlement_WithContext_CreatesDelegate_ThatEvaluatesContext()
    {
        using var entitlements = new TestEntitlements();

        Assert.True(entitlements.HasFeature("export"));
        Assert.True(entitlements.HasFeature("reports"));
        Assert.False(entitlements.HasFeature("unknown"));
    }

    [Fact]
    public void GetMetadata_ReturnsMetadataForEntitlement()
    {
        using var entitlements = new TestEntitlements();

        var metadata = entitlements.GetMetadata(entitlements.CanExport);

        Assert.NotNull(metadata);
        Assert.Equal("CanExport", metadata.Name);
        Assert.Equal("Can export data", metadata.Description);
    }

    [Fact]
    public void GetAllMetadata_ReturnsAllEntitlementMetadata()
    {
        using var entitlements = new TestEntitlements();

        var allMetadata = entitlements.GetAllMetadata().ToList();

        Assert.Equal(4, allMetadata.Count);
        Assert.Contains(allMetadata, m => m.Name == "CanExport");
        Assert.Contains(allMetadata, m => m.Name == "MaxUsers");
        Assert.Contains(allMetadata, m => m.Name == "Tier");
        Assert.Contains(allMetadata, m => m.Name == "HasFeature");
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var entitlements = new TestEntitlements();

        entitlements.Dispose();
        entitlements.Dispose(); // Should not throw
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
            CanExport = DefineEntitlement(nameof(CanExport), () => true, description: "Can export data");
            MaxUsers = DefineEntitlement(nameof(MaxUsers), () => 100, description: "Maximum allowed users");
            Tier = DefineEntitlement(nameof(Tier), () => "premium", description: "Current plan tier");
            HasFeature = DefineEntitlement<string, bool>(
                nameof(HasFeature),
                feature => _enabledFeatures.Contains(feature),
                description: "Check if feature is enabled"
            );
        }
    }

    #endregion
}
