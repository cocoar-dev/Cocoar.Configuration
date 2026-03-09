using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Integration;

/// <summary>
/// Tests for interface deserialization in configuration objects.
/// This addresses the scenario where configuration classes have interface-typed properties
/// that need to be deserialized from JSON (e.g., from environment variables or files).
/// </summary>
public class InterfaceDeserializationTests
{
    // Test interfaces and implementations
    public interface ILoggingConfig
    {
        string LogPath { get; set; }
        Dictionary<string, string> LogLevel { get; set; }
    }

    public class LoggingConfig : ILoggingConfig
    {
        public string LogPath { get; set; } = "";
        public Dictionary<string, string> LogLevel { get; set; } = new();
    }

    public class AppConfiguration
    {
        public string AppName { get; set; } = "";
        public ILoggingConfig Logging { get; set; } = new LoggingConfig();
    }

    [Fact]
    public void Should_Deserialize_Interface_Property_From_StaticJson()
    {
        // Arrange: JSON with interface-typed property
        var json = """
        {
            "AppName": "TestApp",
            "Logging": {
                "LogPath": "/var/log/app.log",
                "LogLevel": {
                    "Default": "Warning",
                    "Microsoft": "Information"
                }
            }
        }
        """;

        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rules: rule => [
                rule.For<AppConfiguration>().FromStaticJson(json)
            ],
            setup: setup => [
                setup.Interface<ILoggingConfig>().DeserializeTo<LoggingConfig>()
            ]));

        // Act
        var config = configManager.GetRequiredConfig<AppConfiguration>();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("TestApp", config.AppName);
        Assert.NotNull(config.Logging);
        Assert.IsType<LoggingConfig>(config.Logging);
        Assert.Equal("/var/log/app.log", config.Logging.LogPath);
        Assert.Equal(2, config.Logging.LogLevel.Count);
        Assert.Equal("Warning", config.Logging.LogLevel["Default"]);
        Assert.Equal("Information", config.Logging.LogLevel["Microsoft"]);
    }

    [Fact]
    public void Should_Deserialize_Interface_Property_From_Environment_Variables()
    {
        // Arrange: Set environment variables that create a nested structure
        Environment.SetEnvironmentVariable("AppName", "EnvTestApp");
        Environment.SetEnvironmentVariable("Logging__LogPath", "/tmp/env.log");
        Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Debug");
        Environment.SetEnvironmentVariable("Logging__LogLevel__System", "Error");

        try
        {
            var configManager = ConfigManager.Create(c => c.UseConfiguration(
                rules: rule => [
                    rule.For<AppConfiguration>().FromEnvironment()
                ],
                setup: setup => [
                    setup.Interface<ILoggingConfig>().DeserializeTo<LoggingConfig>()
                ]));

            // Act
            var config = configManager.GetRequiredConfig<AppConfiguration>();

            // Assert
            Assert.NotNull(config);
            Assert.Equal("EnvTestApp", config.AppName);
            Assert.NotNull(config.Logging);
            Assert.IsType<LoggingConfig>(config.Logging);
            Assert.Equal("/tmp/env.log", config.Logging.LogPath);
            Assert.Equal(2, config.Logging.LogLevel.Count);
            Assert.Equal("Debug", config.Logging.LogLevel["Default"]);
            Assert.Equal("Error", config.Logging.LogLevel["System"]);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("AppName", null);
            Environment.SetEnvironmentVariable("Logging__LogPath", null);
            Environment.SetEnvironmentVariable("Logging__LogLevel__Default", null);
            Environment.SetEnvironmentVariable("Logging__LogLevel__System", null);
        }
    }

    // Additional interface for testing multiple mappings
    public interface IDatabaseConfig
    {
        string ConnectionString { get; set; }
    }

    public class DatabaseConfig : IDatabaseConfig
    {
        public string ConnectionString { get; set; } = "";
    }

    public class ComplexConfiguration
    {
        public ILoggingConfig Logging { get; set; } = new LoggingConfig();
        public IDatabaseConfig Database { get; set; } = new DatabaseConfig();
    }

    [Fact]
    public void Should_Handle_Multiple_Interface_Mappings()
    {

        // Arrange
        var json = """
        {
            "Logging": {
                "LogPath": "/var/log/app.log",
                "LogLevel": {
                    "Default": "Info"
                }
            },
            "Database": {
                "ConnectionString": "Server=localhost;Database=test"
            }
        }
        """;

        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rules: rule => [
                rule.For<ComplexConfiguration>().FromStaticJson(json)
            ],
            setup: setup => [
                setup.Interface<ILoggingConfig>().DeserializeTo<LoggingConfig>(),
                setup.Interface<IDatabaseConfig>().DeserializeTo<DatabaseConfig>()
            ]));

        // Act
        var config = configManager.GetRequiredConfig<ComplexConfiguration>();

        // Assert
        Assert.NotNull(config.Logging);
        Assert.IsType<LoggingConfig>(config.Logging);
        Assert.Equal("/var/log/app.log", config.Logging.LogPath);
        
        Assert.NotNull(config.Database);
        Assert.IsType<DatabaseConfig>(config.Database);
        Assert.Equal("Server=localhost;Database=test", config.Database.ConnectionString);
    }

    // Nested interface types for testing
    public interface IRetryPolicy
    {
        int MaxRetries { get; set; }
        int DelayMs { get; set; }
    }

    public class RetryPolicy : IRetryPolicy
    {
        public int MaxRetries { get; set; }
        public int DelayMs { get; set; }
    }

    public interface IAdvancedLoggingConfig
    {
        string LogPath { get; set; }
        IRetryPolicy RetryPolicy { get; set; }  // Nested interface!
    }

    public class AdvancedLoggingConfig : IAdvancedLoggingConfig
    {
        public string LogPath { get; set; } = "";
        public IRetryPolicy RetryPolicy { get; set; } = new RetryPolicy();
    }

    public class NestedConfiguration
    {
        public string AppName { get; set; } = "";
        public IAdvancedLoggingConfig Logging { get; set; } = new AdvancedLoggingConfig();
    }

    [Fact]
    public void Should_Handle_Nested_Interface_Properties()
    {
        // Arrange: JSON with nested interface properties
        var json = """
        {
            "AppName": "NestedTest",
            "Logging": {
                "LogPath": "/var/log/nested.log",
                "RetryPolicy": {
                    "MaxRetries": 3,
                    "DelayMs": 1000
                }
            }
        }
        """;

        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rules: rule => [
                rule.For<NestedConfiguration>().FromStaticJson(json)
            ],
            setup: setup => [
                setup.Interface<IAdvancedLoggingConfig>().DeserializeTo<AdvancedLoggingConfig>(),
                setup.Interface<IRetryPolicy>().DeserializeTo<RetryPolicy>()
            ]));

        // Act
        var config = configManager.GetRequiredConfig<NestedConfiguration>();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("NestedTest", config.AppName);
        
        Assert.NotNull(config.Logging);
        Assert.IsType<AdvancedLoggingConfig>(config.Logging);
        Assert.Equal("/var/log/nested.log", config.Logging.LogPath);
        
        Assert.NotNull(config.Logging.RetryPolicy);
        Assert.IsType<RetryPolicy>(config.Logging.RetryPolicy);
        Assert.Equal(3, config.Logging.RetryPolicy.MaxRetries);
        Assert.Equal(1000, config.Logging.RetryPolicy.DelayMs);
    }

    // Types for deeply nested testing
    public interface ICircuitBreaker
    {
        int Threshold { get; set; }
    }

    public class CircuitBreaker : ICircuitBreaker
    {
        public int Threshold { get; set; }
    }

    public interface IAdvancedRetryPolicy
    {
        int MaxRetries { get; set; }
        ICircuitBreaker CircuitBreaker { get; set; }  // 3 levels deep!
    }

    public class AdvancedRetryPolicy : IAdvancedRetryPolicy
    {
        public int MaxRetries { get; set; }
        public ICircuitBreaker CircuitBreaker { get; set; } = new CircuitBreaker();
    }

    public interface IDeepLoggingConfig
    {
        string LogPath { get; set; }
        IAdvancedRetryPolicy RetryPolicy { get; set; }
    }

    public class DeepLoggingConfig : IDeepLoggingConfig
    {
        public string LogPath { get; set; } = "";
        public IAdvancedRetryPolicy RetryPolicy { get; set; } = new AdvancedRetryPolicy();
    }

    public class DeepConfiguration
    {
        public IDeepLoggingConfig Logging { get; set; } = new DeepLoggingConfig();
    }

    [Fact]
    public void Should_Handle_Deeply_Nested_Interface_Properties()
    {

        // Arrange: 3 levels of nested interfaces
        var json = """
        {
            "Logging": {
                "LogPath": "/var/log/deep.log",
                "RetryPolicy": {
                    "MaxRetries": 5,
                    "CircuitBreaker": {
                        "Threshold": 10
                    }
                }
            }
        }
        """;

        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rules: rule => [
                rule.For<DeepConfiguration>().FromStaticJson(json)
            ],
            setup: setup => [
                setup.Interface<IDeepLoggingConfig>().DeserializeTo<DeepLoggingConfig>(),
                setup.Interface<IAdvancedRetryPolicy>().DeserializeTo<AdvancedRetryPolicy>(),
                setup.Interface<ICircuitBreaker>().DeserializeTo<CircuitBreaker>()
            ]));

        // Act
        var config = configManager.GetRequiredConfig<DeepConfiguration>();

        // Assert
        Assert.NotNull(config.Logging);
        Assert.Equal("/var/log/deep.log", config.Logging.LogPath);
        Assert.NotNull(config.Logging.RetryPolicy);
        Assert.Equal(5, config.Logging.RetryPolicy.MaxRetries);
        Assert.NotNull(config.Logging.RetryPolicy.CircuitBreaker);
        Assert.Equal(10, config.Logging.RetryPolicy.CircuitBreaker.Threshold);
    }
}



