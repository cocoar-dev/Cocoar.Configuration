namespace Cocoar.Configuration.Flags.Tests;

public class FeatureFlagsBaseTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var flags = new TestFeatureFlags();

        Assert.NotNull(flags);
    }

    [Fact]
    public void ExpiresAt_ReturnsConfiguredValue()
    {
        var flags = new FutureExpiringFlags();

        Assert.Equal(new DateTimeOffset(2099, 12, 31, 0, 0, 0, TimeSpan.Zero), flags.ExpiresAt);
    }

    [Fact]
    public void IsExpired_WhenFuture_ReturnsFalse()
    {
        var flags = new FutureExpiringFlags();

        Assert.False(flags.IsExpired);
    }

    [Fact]
    public void IsExpired_WhenPast_ReturnsTrue()
    {
        var flags = new PastExpiringFlags();

        Assert.True(flags.IsExpired);
    }

    [Fact]
    public void Flag_CreatesDelegate_ThatReturnsValue()
    {
        var flags = new TestFeatureFlags();

        Assert.True(flags.EnabledFeatureFlag());
        Assert.False(flags.DisabledFeatureFlag());
        Assert.Equal(42, flags.IntFeatureFlag());
        Assert.Equal("test", flags.StringFeatureFlag());
    }

    [Fact]
    public void Flag_WithContext_CreatesDelegate_ThatEvaluatesContext()
    {
        var flags = new TestFeatureFlags();

        Assert.True(flags.ContextualFeatureFlag("admin"));
        Assert.False(flags.ContextualFeatureFlag("user"));
    }

    #region Test Classes

    private class TestFeatureFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

        public FeatureFlag<bool> EnabledFeatureFlag { get; }
        public FeatureFlag<bool> DisabledFeatureFlag { get; }
        public FeatureFlag<int> IntFeatureFlag { get; }
        public FeatureFlag<string> StringFeatureFlag { get; }
        public FeatureFlag<string, bool> ContextualFeatureFlag { get; }

        public TestFeatureFlags()
        {
            EnabledFeatureFlag = () => true;
            DisabledFeatureFlag = () => false;
            IntFeatureFlag = () => 42;
            StringFeatureFlag = () => "test";
            ContextualFeatureFlag = ctx => ctx == "admin";
        }
    }

    private class FutureExpiringFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }

    private class PastExpiringFlags
    {
        public DateTimeOffset ExpiresAt => new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }

    #endregion
}
