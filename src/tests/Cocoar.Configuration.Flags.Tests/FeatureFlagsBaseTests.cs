namespace Cocoar.Configuration.Flags.Tests;

public class FeatureFlagsBaseTests
{
    [Fact]
    public void FeatureFlags_CanBeInherited()
    {
        using var flags = new TestFeatureFlags();

        Assert.NotNull(flags);
        Assert.IsAssignableFrom<FeatureFlags>(flags);
    }

    [Fact]
    public void Constructor_WithRegistry_RegistersItself()
    {
        var registry = new FeatureFlagsRegistry();
        using var flags = new TestFeatureFlags(registry);

        Assert.Single(registry.GetAll());
        Assert.Same(flags, registry.Find<TestFeatureFlags>());
    }

    [Fact]
    public void Constructor_WithNullRegistry_DoesNotThrow()
    {
        using var flags = new TestFeatureFlags(null);

        Assert.NotNull(flags);
    }

    [Fact]
    public void Constructor_WithoutRegistry_DoesNotThrow()
    {
        using var flags = new TestFeatureFlags();

        Assert.NotNull(flags);
    }

    [Fact]
    public void ExpiresAt_ReturnsConfiguredValue()
    {
        using var flags = new FutureExpiringFlags();

        Assert.Equal(new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero), flags.ExpiresAt);
    }

    [Fact]
    public void IsExpired_WhenFuture_ReturnsFalse()
    {
        using var flags = new FutureExpiringFlags();

        Assert.False(flags.IsExpired);
    }

    [Fact]
    public void IsExpired_WhenPast_ReturnsTrue()
    {
        using var flags = new PastExpiringFlags();

        Assert.True(flags.IsExpired);
    }

    [Fact]
    public void Flag_CreatesDelegate_ThatReturnsValue()
    {
        using var flags = new TestFeatureFlags();

        Assert.True(flags.EnabledFlag());
        Assert.False(flags.DisabledFlag());
        Assert.Equal(42, flags.IntFlag());
        Assert.Equal("test", flags.StringFlag());
    }

    [Fact]
    public void Flag_WithContext_CreatesDelegate_ThatEvaluatesContext()
    {
        using var flags = new TestFeatureFlags();

        Assert.True(flags.ContextualFlag("admin"));
        Assert.False(flags.ContextualFlag("user"));
    }

    [Fact]
    public void GetMetadata_ReturnsMetadataForFlag()
    {
        using var flags = new TestFeatureFlags();

        var metadata = flags.GetMetadata(flags.EnabledFlag);

        Assert.NotNull(metadata);
        Assert.Equal("EnabledFlag", metadata.Name);
        Assert.Equal("Test enabled flag", metadata.Description);
    }

    [Fact]
    public void GetMetadata_WithPerFlagExpiration_ReturnsOverriddenExpiration()
    {
        using var flags = new TestFeatureFlags();

        var metadata = flags.GetMetadata(flags.FlagWithCustomExpiry);

        Assert.NotNull(metadata);
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero), metadata.ExpiresAt);
    }

    [Fact]
    public void GetMetadata_WithoutPerFlagExpiration_UsesClassExpiration()
    {
        using var flags = new TestFeatureFlags();

        var metadata = flags.GetMetadata(flags.EnabledFlag);

        Assert.NotNull(metadata);
        Assert.Equal(flags.ExpiresAt, metadata.ExpiresAt);
    }

    [Fact]
    public void GetAllMetadata_ReturnsAllFlagMetadata()
    {
        using var flags = new TestFeatureFlags();

        var allMetadata = flags.GetAllMetadata().ToList();

        Assert.Equal(6, allMetadata.Count);
        Assert.Contains(allMetadata, m => m.Name == "EnabledFlag");
        Assert.Contains(allMetadata, m => m.Name == "DisabledFlag");
        Assert.Contains(allMetadata, m => m.Name == "IntFlag");
        Assert.Contains(allMetadata, m => m.Name == "StringFlag");
        Assert.Contains(allMetadata, m => m.Name == "ContextualFlag");
        Assert.Contains(allMetadata, m => m.Name == "FlagWithCustomExpiry");
    }

    [Fact]
    public void GetExpiredFlags_ReturnsOnlyExpiredFlags()
    {
        using var flags = new MixedExpirationFlags();

        var expired = flags.GetExpiredFlags().ToList();

        Assert.Single(expired);
        Assert.Equal("ExpiredFlag", expired[0].Name);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var flags = new TestFeatureFlags();

        flags.Dispose();
        flags.Dispose(); // Should not throw
    }

    #region Test Classes

    private class TestFeatureFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> EnabledFlag { get; }
        public Flag<bool> DisabledFlag { get; }
        public Flag<int> IntFlag { get; }
        public Flag<string> StringFlag { get; }
        public Flag<string, bool> ContextualFlag { get; }
        public Flag<bool> FlagWithCustomExpiry { get; }

        public TestFeatureFlags(IFeatureFlagsRegistry? registry = null) : base(registry)
        {
            EnabledFlag = DefineFlag(nameof(EnabledFlag), () => true, description: "Test enabled flag");
            DisabledFlag = DefineFlag(nameof(DisabledFlag), () => false, description: "Test disabled flag");
            IntFlag = DefineFlag(nameof(IntFlag), () => 42, description: "Test int flag");
            StringFlag = DefineFlag(nameof(StringFlag), () => "test", description: "Test string flag");
            ContextualFlag = DefineFlag<string, bool>(nameof(ContextualFlag), ctx => ctx == "admin", description: "Admin check");
            FlagWithCustomExpiry = DefineFlag(
                nameof(FlagWithCustomExpiry),
                () => true,
                expiresAt: new(2025, 3, 1, 0, 0, 0, TimeSpan.Zero),
                description: "Flag with custom expiry"
            );
        }
    }

    private class FutureExpiringFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public FutureExpiringFlags() : base(null) { }
    }

    private class PastExpiringFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public PastExpiringFlags() : base(null) { }
    }

    private class MixedExpirationFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

        public Flag<bool> ValidFlag { get; }
        public Flag<bool> ExpiredFlag { get; }

        public MixedExpirationFlags() : base(null)
        {
            ValidFlag = DefineFlag(nameof(ValidFlag), () => true, description: "Valid flag");
            ExpiredFlag = DefineFlag(
                nameof(ExpiredFlag),
                () => true,
                expiresAt: new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
                description: "Expired flag"
            );
        }
    }

    #endregion
}
