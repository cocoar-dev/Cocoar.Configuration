using Cocoar.Configuration.Core.Tests.TestUtilities;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Core.Tests.Managers;

public class ConfigManagerDeserializationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();
    private readonly TestLogger _logger = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignore disposal errors in tests
            }
        }
        _disposables.Clear();
    }

    private void TrackForDisposal(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    public class ConfigWithRequired
    {
        public required string Name { get; set; }
        public int Value { get; set; }
    }

    public class ConfigWithInt
    {
        public int Count { get; set; }
    }

    public class SimpleConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "High")]
    public void GetConfig_MissingRequiredProperty_ReturnsNullAndLogsError()
    {
        // Arrange: JSON is missing the required "Name" property
        var json = """{"Value": 42}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act
        var result = configManager.GetConfig<ConfigWithRequired>();

        // Assert
        Assert.Null(result);
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "ConfigWithRequired"),
            "Expected error log entry containing type name 'ConfigWithRequired'");
        Assert.True(_logger.HasLogEntry(LogLevel.Error, 5100),
            "Expected error log entry with EventId 5100");
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "High")]
    public void GetRequiredConfig_MissingRequiredProperty_ThrowsInvalidOperationException()
    {
        // Arrange: JSON is missing the required "Name" property
        var json = """{"Value": 42}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => configManager.GetRequiredConfig<ConfigWithRequired>());

        Assert.Contains("ConfigWithRequired", exception.Message);
        Assert.Contains("hasn't been loaded yet", exception.Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "High")]
    public void GetConfig_ValidJson_ReturnsInstanceWithNoErrorLog()
    {
        // Arrange: JSON has all required properties
        var json = """{"Name": "test", "Value": 42}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act
        var result = configManager.GetConfig<ConfigWithRequired>();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
        Assert.False(_logger.HasLogEntry(LogLevel.Error, "deserialize"),
            "No error should be logged for successful deserialization");
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "Medium")]
    public void GetConfig_TypeMismatch_ReturnsNullAndLogsError()
    {
        // Arrange: JSON has a string where an int is expected
        var json = """{"Count": "not-a-number"}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithInt>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act
        var result = configManager.GetConfig<ConfigWithInt>();

        // Assert
        Assert.Null(result);
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "ConfigWithInt"),
            "Expected error log entry containing type name 'ConfigWithInt'");
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "Medium")]
    public void GetConfig_DeserializationFailure_LogContainsJsonContent()
    {
        // Arrange: JSON is missing required property
        var json = """{"Value": 123}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act
        var result = configManager.GetConfig<ConfigWithRequired>();

        // Assert
        Assert.Null(result);
        var logEntry = _logger.FindEntry(LogLevel.Error, "ConfigWithRequired");
        Assert.NotNull(logEntry);
        Assert.Contains("123", logEntry.Message); // JSON content should be in the log
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "Medium")]
    public void TryGetConfig_MissingRequiredProperty_ReturnsFalse()
    {
        // Arrange: JSON is missing the required "Name" property
        var json = """{"Value": 42}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act
        var success = configManager.TryGetConfig<ConfigWithRequired>(out var result);

        // Assert
        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "Low")]
    public void GetConfig_SimpleConfig_WorksWithDefaultValues()
    {
        // Arrange: Simple config without required properties
        var json = """{"Name": "test"}""";

        var configManager = new ConfigManager(
            r => [r.For<SimpleConfig>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act
        var result = configManager.GetConfig<SimpleConfig>();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(0, result.Value); // Default value
        Assert.False(_logger.HasLogEntry(LogLevel.Error, "deserialize"),
            "No error should be logged for successful deserialization");
    }
}
