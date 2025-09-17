using System.Text.Json;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cocoar.Configuration.Tests.Providers;

public class StaticJsonProviderTests
{
    private record TestConfig(string Name, int Value);
    private record OtherConfig(string Description, bool Enabled);

    [Fact]
    public void CreateRule_WithJsonElement_ShouldCreateValidRule()
    {
        // Arrange
        var json = JsonDocument.Parse("""{"Name": "Test", "Value": 42}""").RootElement;

        // Act
        var rule = StaticJsonProvider.CreateRule<TestConfig>(json);

        // Assert
        Assert.NotNull(rule);
        Assert.Equal(typeof(StaticJsonProvider), rule.ProviderType);
    }

    [Fact]
    public void CreateRule_WithJsonString_ShouldCreateValidRule()
    {
        // Arrange
        var jsonString = """{"Name": "Test", "Value": 42}""";

        // Act
        var rule = StaticJsonProvider.CreateRule<TestConfig>(jsonString);

        // Assert
        Assert.NotNull(rule);
        Assert.Equal(typeof(StaticJsonProvider), rule.ProviderType);
    }

    [Fact]
    public void CreateRule_WithJsonString_ShouldParseCorrectly()
    {
        // Arrange
        var jsonString = """{"Name": "TestApp", "Value": 123}""";
        
        // Act
        var rule = StaticJsonProvider.CreateRule<TestConfig>(jsonString);
        var configManager = new ConfigManager([rule], null, NullLogger.Instance).Initialize();
        var success = configManager.TryGetConfig<TestConfig>(out var config);

        // Assert
        Assert.True(success);
        Assert.NotNull(config);
        Assert.Equal("TestApp", config.Name);
        Assert.Equal(123, config.Value);
    }

    [Fact]
    public void MultipleRules_WithDifferentJsonStrings_ShouldNotShareProviderInstances()
    {
        // Arrange - This test prevents the bug we discovered where providers were shared
        var json1 = """{"Name": "Config1", "Value": 1}""";
        var json2 = """{"Description": "Config2", "Enabled": true}""";

        // Act
        var rule1 = StaticJsonProvider.CreateRule<TestConfig>(json1);
        var rule2 = StaticJsonProvider.CreateRule<OtherConfig>(json2);
        
        var configManager = new ConfigManager([rule1, rule2], null, NullLogger.Instance).Initialize();
        
        var success1 = configManager.TryGetConfig<TestConfig>(out var config1);
        var success2 = configManager.TryGetConfig<OtherConfig>(out var config2);

        // Assert - Each rule should get its own data, not shared instances
        Assert.True(success1);
        Assert.NotNull(config1);
        Assert.Equal("Config1", config1.Name);
        Assert.Equal(1, config1.Value);
        
        Assert.True(success2);
        Assert.NotNull(config2);
        Assert.Equal("Config2", config2.Description);
        Assert.True(config2.Enabled);
    }

    [Fact]
    public void MultipleRules_WithSameJsonString_ShouldNotShareProviderInstances()
    {
        // Arrange - Even identical JSON should create separate providers
        var json = """{"Name": "SameData", "Value": 999}""";

        // Act
        var rule1 = StaticJsonProvider.CreateRule<TestConfig>(json);
        var rule2 = StaticJsonProvider.CreateRule<TestConfig>(json); // Same JSON, same type
        
        var configManager = new ConfigManager([rule1, rule2], null, NullLogger.Instance).Initialize();
        
        // This would have failed in the old implementation due to provider sharing
        var success = configManager.TryGetConfig<TestConfig>(out var config);

        // Assert
        Assert.True(success);
        Assert.NotNull(config);
        Assert.Equal("SameData", config.Name);
        Assert.Equal(999, config.Value);
    }

    [Fact]
    public void StaticJsonProviderOptions_GenerateProviderKey_ShouldReturnNull()
    {
        // Arrange - Test the null-key pattern that prevents sharing
        var json = JsonDocument.Parse("""{"Name": "Test", "Value": 42}""").RootElement;
        var options = new StaticJsonProviderOptions(json);

        // Act
        var key = options.GenerateProviderKey();

        // Assert - Null key means no sharing
        Assert.Null(key);
    }

    [Fact]
    public void FluentAPI_StaticJson_ShouldWork()
    {
        // Arrange
        var jsonString = """{"Name": "FluentTest", "Value": 456}""";

        // Act
        var rule = Rule.From.StaticJson(jsonString).For<TestConfig>();
        var configManager = new ConfigManager([rule], null, NullLogger.Instance).Initialize();
        var success = configManager.TryGetConfig<TestConfig>(out var config);

        // Assert
        Assert.True(success);
        Assert.NotNull(config);
        Assert.Equal("FluentTest", config.Name);
        Assert.Equal(456, config.Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Name": null, "Value": 0}""")]
    [InlineData("""{"Name": "", "Value": -1}""")]
    public void CreateRule_WithVariousJsonInputs_ShouldHandleGracefully(string jsonInput)
    {
        // Act & Assert - Should not throw
        var rule = StaticJsonProvider.CreateRule<TestConfig>(jsonInput);
        Assert.NotNull(rule);
        
        // Verify it can be used
        var configManager = new ConfigManager([rule], null, NullLogger.Instance).Initialize();
        var success = configManager.TryGetConfig<TestConfig>(out var config);
        Assert.True(success);
        Assert.NotNull(config);
    }

    [Fact]
    public void CreateRule_WithInvalidJson_ShouldThrow()
    {
        // Arrange
        var invalidJson = """{"Name": "Test", "Value":}"""; // Missing value

        // Act & Assert - JsonReaderException is a subclass of JsonException
        Assert.ThrowsAny<JsonException>(() => 
            StaticJsonProvider.CreateRule<TestConfig>(invalidJson));
    }
}