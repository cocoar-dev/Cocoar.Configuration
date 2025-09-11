using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Tests;

public class ConfigurationAnalyzerTests
{
    private readonly ITestOutputHelper _output;

    public ConfigurationAnalyzerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void AnalyzeDependencies_LogsWarningForStaticAndDynamicProviders()
    {
        // Arrange
        var logMessages = new List<string>();
        var logger = new TestLogger(logMessages);

        var rules = new ConfigRule[]
        {
            Rule.From.Static(_ => new { Value = 42 })
                .For<object>()
                .Build(),
            Rule.From.Environment(_ => new Cocoar.Configuration.Fluent.ProviderOptions.EnvironmentVariableRuleOptions())
                .For<string>()
                .Build()
        };

        // Act
        ConfigurationAnalyzer.AnalyzeDependencies(rules, logger);

        // Assert
        Assert.Contains(logMessages, msg => msg.Contains("static seed rules") && msg.Contains("dynamic providers"));
        
        foreach (var msg in logMessages)
        {
            _output.WriteLine($"LOG: {msg}");
        }
    }

    [Fact]
    public void AnalyzeDependencies_LogsRuleSummary()
    {
        // Arrange
        var logMessages = new List<string>();
        var logger = new TestLogger(logMessages);

        var rules = new ConfigRule[]
        {
            Rule.From.Environment(_ => new Cocoar.Configuration.Fluent.ProviderOptions.EnvironmentVariableRuleOptions())
                .For<TestConfig>()
                .Required()
                .Build(),
            Rule.From.Environment(_ => new Cocoar.Configuration.Fluent.ProviderOptions.EnvironmentVariableRuleOptions())
                .For<TestConfig>()
                .Optional()
                .Build()
        };

        // Act
        ConfigurationAnalyzer.AnalyzeDependencies(rules, logger);

        // Assert
        Assert.Contains(logMessages, msg => msg.Contains("required rules") && msg.Contains("optional rules"));
        Assert.Contains(logMessages, msg => msg.Contains("TestConfig"));
        
        foreach (var msg in logMessages)
        {
            _output.WriteLine($"LOG: {msg}");
        }
    }

    [Fact]
    public void ConfigManager_Initialize_RunsAnalysis()
    {
        // Arrange
        var logMessages = new List<string>();
        var logger = new TestLogger(logMessages);

        var rules = new ConfigRule[]
        {
            Rule.From.Static(_ => new TestConfig { Value = "test" })
                .For<TestConfig>()
                .Build()
        };

        // Act
        var manager = new ConfigManager(rules, logger).Initialize();

        // Assert
        Assert.Contains(logMessages, msg => msg.Contains("static seed rules") || msg.Contains("Configuration rule summary"));
        
        foreach (var msg in logMessages)
        {
            _output.WriteLine($"MANAGER LOG: {msg}");
        }
    }

    private class TestConfig
    {
        public string Value { get; set; } = "";
    }

    private class TestLogger : ILogger
    {
        private readonly List<string> _messages;

        public TestLogger(List<string> messages)
        {
            _messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Add($"[{logLevel}] {formatter(state, exception)}");
        }

        private class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
