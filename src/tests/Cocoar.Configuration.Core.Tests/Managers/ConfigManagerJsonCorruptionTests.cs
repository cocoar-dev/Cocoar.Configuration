using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Reactive.Linq;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Providers.Abstractions;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Managers;
public class ConfigManagerJsonCorruptionTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

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

    public class TestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    /// <summary>
    /// Test provider that can return corrupted JSON to simulate file corruption scenarios.
    /// </summary>
    private class JsonCorruptionProvider : ConfigurationProvider<JsonCorruptionProviderOptions, JsonCorruptionProviderQuery>
    {
        public JsonCorruptionProvider(JsonCorruptionProviderOptions options) : base(options)
        {
        }

        public override Task<byte[]> FetchConfigurationBytesAsync(JsonCorruptionProviderQuery query, CancellationToken ct = default)
        {
            if (query.ReturnCorruptJson)
            {
                // This will cause JsonDocument.Parse to throw JsonException
                var corruptJson = ProviderOptions.CorruptJsonString;
                var document = JsonDocument.Parse(corruptJson); // This will throw!
                var _bytes = JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
                return Task.FromResult(_bytes);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(ProviderOptions.ValidJsonData);

            return Task.FromResult(bytes);
        }

        public override IObservable<byte[]> ChangesAsBytes(JsonCorruptionProviderQuery query) => Observable.Empty<byte[]>();
    }

    private class JsonCorruptionProviderOptions : IProviderConfiguration
    {
        public JsonElement ValidJsonData { get; }
        public string CorruptJsonString { get; }

        public JsonCorruptionProviderOptions(string validJson, string corruptJson)
        {
            using var document = JsonDocument.Parse(validJson);
            ValidJsonData = document.RootElement.Clone();
            CorruptJsonString = corruptJson;
        }

        public string? GenerateProviderKey() => null;
    }

    private class JsonCorruptionProviderQuery : IProviderQuery
    {
        public bool ReturnCorruptJson { get; }

        public JsonCorruptionProviderQuery(bool returnCorruptJson = false)
        {
            ReturnCorruptJson = returnCorruptJson;
        }
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_RequiredRuleWithCorruptJson_ThrowsJsonException()
    {

        var corruptJsonString = """{"Name": "Test", "Value": invalid_number}"""; // Invalid JSON
        var options = new JsonCorruptionProviderOptions(
            validJson: """{"Name": "Good", "Value": 100}""",
            corruptJson: corruptJsonString);

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<JsonCorruptionProvider, JsonCorruptionProviderOptions, JsonCorruptionProviderQuery>(
                options,
                new(returnCorruptJson: true), // Will try to parse corrupt JSON
                typeof(TestConfig),
                new(Required: true))
        };

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);


        var exception = Assert.Throws<InvalidOperationException>(() => configManager.Initialize());
        
        // The inner exception should be a JSON-related exception from the malformed JSON
        Assert.NotNull(exception.InnerException);
        Assert.True(exception.InnerException is JsonException || 
                   exception.InnerException.GetType().Name.Contains("JsonReader"), 
            $"Expected JSON-related exception, but got {exception.InnerException.GetType().Name}");
        Assert.Contains("JsonCorruptionProvider", exception.Message);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "High")]
    public void ConfigManager_OptionalRuleWithCorruptJson_SkipsCorruptRuleAndContinues()
    {

        var corruptJsonString = """{"Name": "Test", "broken": }"""; // Invalid JSON syntax
        var validJsonString = """{"Name": "ValidRule", "Value": 200}""";

        var rules = new List<ConfigRule>
        {
            // First rule: Optional with corrupt JSON - should be skipped
            ConfigRule.Create<JsonCorruptionProvider, JsonCorruptionProviderOptions, JsonCorruptionProviderQuery>(
                new(validJsonString, corruptJsonString),
                new JsonCorruptionProviderQuery(returnCorruptJson: true),
                typeof(TestConfig),
                new(Required: false)), // Optional

            // Second rule: Valid JSON - should be used
            ConfigRule.Create<JsonCorruptionProvider, JsonCorruptionProviderOptions, JsonCorruptionProviderQuery>(
                new(validJsonString, corruptJsonString),
                new JsonCorruptionProviderQuery(returnCorruptJson: false), // Good JSON
                typeof(TestConfig),
                new(Required: false))
        };

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);


        configManager.Initialize();
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("ValidRule", config.Name);
        Assert.Equal(200, config.Value);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Feature", "ErrorHandling")]
    [Trait("Priority", "Medium")]
    public void ConfigManager_ProviderFailureVsJsonCorruption_BothHandledSimilarly()
    {

        var rules = new List<ConfigRule>
        {
            // Rule 1: Provider-level failure (using FailableProvider)
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.AlwaysFail("""{"Name": "WontWork", "Value": 1}"""),
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: false)),

            // Rule 2: JSON-level failure (using JsonCorruptionProvider)
            ConfigRule.Create<JsonCorruptionProvider, JsonCorruptionProviderOptions, JsonCorruptionProviderQuery>(
                new(
                    validJson: """{"Name": "Good", "Value": 2}""",
                    corruptJson: """{"Name": "Bad", "Value": corrupt}"""),
                new JsonCorruptionProviderQuery(returnCorruptJson: true),
                typeof(TestConfig),
                new(Required: false)),

            // Rule 3: Working rule
            ConfigRule.Create<FailableProvider, FailableProviderOptions, FailableProviderQuery>(
                FailableProviderOptions.AlwaysSucceed("""{"Name": "Success", "Value": 999}"""),
                FailableProviderQuery.Success,
                typeof(TestConfig),
                new(Required: false))
        };

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance);
        TrackForDisposal(configManager);


        configManager.Initialize();
        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("Success", config.Name);
        Assert.Equal(999, config.Value);
    }
}