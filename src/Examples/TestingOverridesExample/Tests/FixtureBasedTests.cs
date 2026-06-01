using Cocoar.Configuration.Testing;
using Cocoar.Configuration.Providers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Examples.TestingOverridesExample.Tests;

/// <summary>
/// Shared test fixture that holds configuration context.
/// The context is created once and reused across all test classes using this fixture.
/// </summary>
public class SharedIntegrationTestFixture
{
    /// <summary>
    /// Pre-built test configuration context built via <see cref="TestOverrideBuilder"/> (fixture pattern).
    /// </summary>
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=fixture-test-db;Database=FixtureTestDb;",
                MaxConnections = 10
            }),
            rule.For<ApiSettings>().FromStatic(_ => new ApiSettings
            {
                BaseUrl = "https://api.fixture-test.example.com",
                ApiKey = "fixture-test-api-key"
            })
        ]);
}

/// <summary>
/// Demonstrates the fixture-based pattern for sharing test configuration.
/// The constructor applies the fixture's context to bridge the AsyncLocal gap.
/// </summary>
public class FixtureBasedIntegrationTests : IClassFixture<SharedIntegrationTestFixture>, IDisposable
{
    private readonly SharedIntegrationTestFixture _fixture;

    public FixtureBasedIntegrationTests(SharedIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        // Bridge the async context gap - applies fixture's context to test's async context
        CocoarTestConfiguration.Apply(_fixture.TestContext);
    }

    public void Dispose()
    {
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public async Task Test1_UsesFixtureConfiguration()
    {
        // Arrange & Act - Create WebApplicationFactory
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        // Assert
        var response = await client.GetAsync("/config");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("fixture-test-db", content);
        Assert.Contains("api.fixture-test.example.com", content);
        Assert.Contains("fixture-test-api-key", content);
    }

    [Fact]
    public async Task Test2_AlsoUsesFixtureConfiguration()
    {
        // Same configuration as Test1, no repetition needed

        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/config");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("fixture-test-db", content);
    }

    [Fact]
    public void Test3_VerifiesConfigurationIsActive()
    {
        // The configuration should be active in test methods
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.Equal(TestConfigurationMode.Replace, CocoarTestConfiguration.Current!.ConfigurationMode);
    }
}

/// <summary>
/// Demonstrates using TestOverrideBuilder (disposable scope) for automatic cleanup.
/// </summary>
public class ScopeBasedTests
{
    [Fact]
    public void Scope_ClearsConfigurationOnDispose()
    {
        // Arrange
        Assert.False(CocoarTestConfiguration.IsActive);

        // Act - Create scope
        using (var scope = CocoarTestConfiguration.ReplaceConfiguration(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { ConnectionString = "test" })
        ]))
        {
            Assert.True(CocoarTestConfiguration.IsActive);
        }

        // Assert - Automatically cleared
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void Scope_ClearsConfigurationEvenOnException()
    {
        // Arrange
        Assert.False(CocoarTestConfiguration.IsActive);

        try
        {
            using var scope = CocoarTestConfiguration.ReplaceConfiguration(rule => [
                rule.For<DbConfig>().FromStatic(_ => new DbConfig { ConnectionString = "test" })
            ]);

            Assert.True(CocoarTestConfiguration.IsActive);
            throw new InvalidOperationException("Simulated test failure");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert - Still cleared even though exception was thrown
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void AppendConfiguration_ReturnsScope()
    {
        Assert.False(CocoarTestConfiguration.IsActive);

        using (var scope = CocoarTestConfiguration.AppendConfiguration(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { ConnectionString = "test" })
        ]))
        {
            Assert.True(CocoarTestConfiguration.IsActive);
            Assert.Equal(TestConfigurationMode.Append, CocoarTestConfiguration.Current!.ConfigurationMode);
        }

        Assert.False(CocoarTestConfiguration.IsActive);
    }
}

/// <summary>
/// Tests for TestConfigurationContext factory methods and TestOverrideBuilder.
/// </summary>
public class TestConfigurationContextFactoryTests
{
    [Fact]
    public void Replace_CreatesContextInReplaceMode()
    {
        // Act
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { ConnectionString = "test" })
        ]);

        // Assert
        Assert.Equal(TestConfigurationMode.Replace, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
    }

    [Fact]
    public void Append_CreatesContextInAppendMode()
    {
        // Act
        var context = TestConfigurationContext.Append(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { ConnectionString = "test" })
        ]);

        // Assert
        Assert.Equal(TestConfigurationMode.Append, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
    }

    [Fact]
    public void Apply_SetsContextFromExistingInstance()
    {
        // Arrange
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { ConnectionString = "applied-test" })
        ]);

        Assert.False(CocoarTestConfiguration.IsActive);

        // Act
        using var scope = CocoarTestConfiguration.Apply(context);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.Same(context, CocoarTestConfiguration.Current);
    }

    [Fact]
    public void Apply_ThrowsOnNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => CocoarTestConfiguration.Apply(null!));
    }

    [Fact]
    public void Replace_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TestConfigurationContext.Replace(null!));
    }

    [Fact]
    public void Append_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TestConfigurationContext.Append(null!));
    }

    [Fact]
    public void TestOverrideBuilder_ReplaceConfiguration_ThrowsOnNullRules()
    {
        // The public constructor is for the fixture pattern; null rules should throw
        Assert.Throws<ArgumentNullException>(() =>
            new TestOverrideBuilder().ReplaceConfiguration(null!));
    }
}
