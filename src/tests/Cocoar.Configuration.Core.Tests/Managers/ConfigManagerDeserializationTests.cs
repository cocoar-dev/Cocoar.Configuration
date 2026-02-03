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
    public void Initialize_MissingRequiredProperty_ThrowsDeserializationException()
    {
        // Arrange: JSON is missing the required "Name" property
        var json = """{"Value": 42}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);

        // Act & Assert: With Master Backplane, deserialization failures at startup throw
        var exception = Assert.Throws<ConfigurationDeserializationException>(
            () => configManager.Initialize());

        Assert.Single(exception.Failures);
        Assert.Equal(typeof(ConfigWithRequired), exception.Failures[0].ConfigType);
        Assert.Contains("Name", exception.Failures[0].Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "High")]
    public void Initialize_MissingRequiredProperty_ExceptionContainsJsonPreview()
    {
        // Arrange: JSON is missing the required "Name" property
        var json = """{"Value": 42}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);

        // Act & Assert
        var exception = Assert.Throws<ConfigurationDeserializationException>(
            () => configManager.Initialize());

        // The exception should include a JSON preview
        Assert.NotNull(exception.Failures[0].JsonPreview);
        Assert.Contains("42", exception.Failures[0].JsonPreview);
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
    public void Initialize_TypeMismatch_ThrowsDeserializationException()
    {
        // Arrange: JSON has a string where an int is expected
        var json = """{"Count": "not-a-number"}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithInt>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);

        // Act & Assert: With Master Backplane, deserialization failures at startup throw
        var exception = Assert.Throws<ConfigurationDeserializationException>(
            () => configManager.Initialize());

        Assert.Single(exception.Failures);
        Assert.Equal(typeof(ConfigWithInt), exception.Failures[0].ConfigType);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "Medium")]
    public void Initialize_MultipleFailures_ThrowsExceptionWithAllFailures()
    {
        // Arrange: Both configs will fail
        var json1 = """{"Value": 123}"""; // Missing Name
        var json2 = """{"Count": "not-a-number"}"""; // Type mismatch

        var configManager = new ConfigManager(
            r => [
                r.For<ConfigWithRequired>().FromStaticJson(json1),
                r.For<ConfigWithInt>().FromStaticJson(json2)
            ],
            logger: _logger);
        TrackForDisposal(configManager);

        // Act & Assert
        var exception = Assert.Throws<ConfigurationDeserializationException>(
            () => configManager.Initialize());

        Assert.Equal(2, exception.Failures.Count);
        Assert.Contains(exception.Failures, f => f.ConfigType == typeof(ConfigWithRequired));
        Assert.Contains(exception.Failures, f => f.ConfigType == typeof(ConfigWithInt));
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

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "Deserialization")]
    [Trait("Priority", "High")]
    public void GetConfig_AfterSuccessfulInit_ReturnsCachedInstance()
    {
        // Arrange: Valid JSON
        var json = """{"Name": "test", "Value": 42}""";

        var configManager = new ConfigManager(
            r => [r.For<ConfigWithRequired>().FromStaticJson(json)],
            logger: _logger);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Act: Get config multiple times
        var result1 = configManager.GetConfig<ConfigWithRequired>();
        var result2 = configManager.GetConfig<ConfigWithRequired>();

        // Assert: Same instance returned (no re-deserialization)
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Same(result1, result2);
    }
}
