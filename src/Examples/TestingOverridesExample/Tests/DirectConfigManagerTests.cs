using Cocoar.Configuration.Core;
using Cocoar.Configuration.Testing;
using Cocoar.Configuration.Providers;
using Xunit;

namespace Examples.TestingOverridesExample.Tests;

public class DirectConfigManagerTests : IDisposable
{
    [Fact]
    public void DirectConfigManager_AppliesTestOverrides_ReplaceMode()
    {
        // Arrange - Set test configuration BEFORE creating ConfigManager
        CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=test-direct;Database=DirectTest;",
                MaxConnections = 42
            })
        ]);

        // Act - Create ConfigManager directly (no DI, no AspNetCore)
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            rule.For<DbConfig>().FromFile("config.json").Select("Database") // This will be SKIPPED
        ]));

        var dbConfig = configManager.GetConfig<DbConfig>()!;

        // Assert - Test rules were used, not config.json
        Assert.Equal("Server=test-direct;Database=DirectTest;", dbConfig.ConnectionString);
        Assert.Equal(42, dbConfig.MaxConnections);
    }

    [Fact]
    public void DirectConfigManager_AppliesTestOverrides_AppendMode()
    {
        // Arrange
        CocoarTestConfiguration.AppendTestRules(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                MaxConnections = 999 // Override only MaxConnections
            })
        ]);

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=base;",
                MaxConnections = 10
            })
        ]));

        var dbConfig = configManager.GetConfig<DbConfig>()!;

        // Assert - Base rule + test override merged (last-write-wins)
        Assert.Equal(999, dbConfig.MaxConnections); // From test override
    }

    [Fact]
    public void DirectConfigManager_WorksNormally_WhenNoTestOverride()
    {
        // No CocoarTestConfiguration set

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=normal;",
                MaxConnections = 50
            })
        ]));

        var dbConfig = configManager.GetConfig<DbConfig>()!;

        // Assert - Normal behavior
        Assert.Equal("Server=normal;", dbConfig.ConnectionString);
        Assert.Equal(50, dbConfig.MaxConnections);
    }

    public void Dispose()
    {
        CocoarTestConfiguration.Clear();
    }
}
