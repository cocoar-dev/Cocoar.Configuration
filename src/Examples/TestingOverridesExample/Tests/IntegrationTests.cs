using Cocoar.Configuration.Testing;
using Cocoar.Configuration.Providers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Examples.TestingOverridesExample.Tests;

public class IntegrationTestsWithReplace : IDisposable
{
    [Fact]
    public async Task ReplaceAllRules_OverridesAllConfiguration()
    {
        // Arrange - Set test configuration BEFORE creating WebApplicationFactory
        CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=test-db;Database=TestDb;",
                MaxConnections = 5
            }),
            rule.For<ApiSettings>().FromStatic(_ => new ApiSettings
            {
                BaseUrl = "https://api.test.example.com",
                ApiKey = "test-api-key"
            })
        ]);

        // Act - Create WebApplicationFactory (config.json will be SKIPPED)
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        // Assert
        var response = await client.GetAsync("/config");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("test-db", content);
        Assert.Contains("api.test.example.com", content);
        Assert.Contains("test-api-key", content);

        // Verify production values are NOT present
        Assert.DoesNotContain("production-db", content);
        Assert.DoesNotContain("prod-api-key", content);
    }

    public void Dispose()
    {
        CocoarTestConfiguration.Clear();
    }
}

public class IntegrationTestsWithAppend : IDisposable
{
    [Fact]
    public async Task AppendTestRules_OverridesSpecificValues()
    {
        // Arrange - Append test rules (config.json runs first, then test rules override)
        CocoarTestConfiguration.AppendTestRules(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=test-db;Database=TestDb;",
                // MaxConnections not specified - will use value from config.json
            })
        ]);

        // Act
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        // Assert
        var response = await client.GetAsync("/config");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();

        // DbConfig.ConnectionString overridden by test
        Assert.Contains("test-db", content);

        // ApiSettings comes from config.json (not overridden in test)
        Assert.Contains("api.production.example.com", content);
        Assert.Contains("prod-api-key", content);
    }

    public void Dispose()
    {
        CocoarTestConfiguration.Clear();
    }
}

public class IntegrationTestsRealWorldScenario
{
    [Fact]
    public async Task ReplaceMode_PreventFailureFromMissingHttpEndpoint()
    {
        // Scenario: App normally polls HTTP endpoint that's unavailable in tests
        // Replace mode prevents HTTP provider from running at all

        CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<ApiSettings>().FromStatic(_ => new ApiSettings
            {
                BaseUrl = "https://test.local",
                ApiKey = "test-key"
            })
        ]);

        await using var factory = new WebApplicationFactory<Program>();

        // Success - HTTP provider never attempted to connect
        Assert.True(CocoarTestConfiguration.IsActive);

        CocoarTestConfiguration.Clear();
    }
}
