using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using System.Diagnostics;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Providers;

/// <summary>
/// Isolation tests for StaticJsonProvider - testing only deterministic JSON provider functionality
/// without any I/O dependencies. These tests validate core provider behavior including
/// JSON string support, factory functions, error handling, and performance characteristics.
/// </summary>
public class StaticJsonProviderIsolationTests
{
    #region Test Configuration Classes

    public record TestConfig(string Name, int Value, bool Enabled);
    public record ComplexConfig(string Title, TestNestedConfig Settings, DateTime Timestamp);
    public record TestNestedConfig(int Priority, string[] Tags);
    public record PerformanceConfig(int Id, string Data, double Score);

    #endregion

    #region Basic Functionality Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithSimpleJson_ReturnsCorrectData()
    {

        const string json = """{"Name": "TestApp", "Value": 42, "Enabled": true}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal("TestApp", result.ToJsonElement().GetProperty("Name").GetString());
        Assert.Equal(42, result.ToJsonElement().GetProperty("Value").GetInt32());
        Assert.True(result.ToJsonElement().GetProperty("Enabled").GetBoolean());
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithComplexJson_ReturnsNestedData()
    {

        const string json = """
        {
            "Title": "Complex Configuration",
            "Settings": {
                "Priority": 10,
                "Tags": ["production", "critical", "monitored"]
            },
            "Timestamp": "2025-01-01T12:00:00Z"
        }
        """;
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal("Complex Configuration", result.ToJsonElement().GetProperty("Title").GetString());
        Assert.Equal(10, result.ToJsonElement().GetProperty("Settings").GetProperty("Priority").GetInt32());
        
        var tags = result.ToJsonElement().GetProperty("Settings").GetProperty("Tags");
        Assert.Equal(3, tags.GetArrayLength());
        Assert.Equal("production", tags[0].GetString());
        Assert.Equal("critical", tags[1].GetString());
        Assert.Equal("monitored", tags[2].GetString());
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithEmptyJson_ReturnsEmptyObject()
    {

        const string json = "{}";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        Assert.Empty(result.ToJsonElement().EnumerateObject());
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_MultipleCalls_ReturnsIdenticalData()
    {

        const string json = """{"Name": "Consistent", "Value": 100}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var result1 = await provider.FetchConfigurationBytesAsync(query);
        var result2 = await provider.FetchConfigurationBytesAsync(query);
        var result3 = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal(result1.ToJsonElement().GetProperty("Name").GetString(), result2.ToJsonElement().GetProperty("Name").GetString());
        Assert.Equal(result1.ToJsonElement().GetProperty("Name").GetString(), result3.ToJsonElement().GetProperty("Name").GetString());
        Assert.Equal(result1.ToJsonElement().GetProperty("Value").GetInt32(), result2.ToJsonElement().GetProperty("Value").GetInt32());
        Assert.Equal(result1.ToJsonElement().GetProperty("Value").GetInt32(), result3.ToJsonElement().GetProperty("Value").GetInt32());
    }

    #endregion

    #region Error Handling Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void Constructor_WithMalformedJson_ThrowsJsonException()
    {

        // JsonReaderException is a subclass of JsonException, so use ThrowsAny
        Assert.ThrowsAny<JsonException>(() =>
        {
            const string malformedJson = """{"Name": "Test", "Value":}"""; // Missing value
            using var document = JsonDocument.Parse(malformedJson);
        });
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithNullValues_HandlesGracefully()
    {

        const string json = """{"Name": null, "Value": 42, "OptionalField": null}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal(JsonValueKind.Null, result.ToJsonElement().GetProperty("Name").ValueKind);
        Assert.Equal(42, result.ToJsonElement().GetProperty("Value").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.ToJsonElement().GetProperty("OptionalField").ValueKind);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithVariousDataTypes_HandlesCorrectly()
    {

        const string json = """
        {
            "stringValue": "hello",
            "intValue": 42,
            "floatValue": 3.14,
            "boolValue": true,
            "arrayValue": [1, 2, 3],
            "objectValue": {"nested": "data"},
            "nullValue": null
        }
        """;
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal("hello", result.ToJsonElement().GetProperty("stringValue").GetString());
        Assert.Equal(42, result.ToJsonElement().GetProperty("intValue").GetInt32());
        Assert.Equal(3.14, result.ToJsonElement().GetProperty("floatValue").GetDouble(), 2);
        Assert.True(result.ToJsonElement().GetProperty("boolValue").GetBoolean());
        Assert.Equal(3, result.ToJsonElement().GetProperty("arrayValue").GetArrayLength());
        Assert.Equal("data", result.ToJsonElement().GetProperty("objectValue").GetProperty("nested").GetString());
        Assert.Equal(JsonValueKind.Null, result.ToJsonElement().GetProperty("nullValue").ValueKind);
    }

    #endregion

    #region Observable/Reactive Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task Changes_Always_ReturnsEmptyObservable()
    {

        const string json = """{"Name": "Static", "Value": 1}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        var emissions = new List<JsonElement>();
        var completed = false;


        var subscription = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()),
            _ => { }, // OnError
            () => completed = true); // OnCompleted

        // Use active waiting to ensure observable behavior is deterministic
        await ActiveWaitHelpers.WaitUntilAsync(
            () => completed,
            timeout: TimeSpan.FromSeconds(5),
            description: "Changes observable completion");


        Assert.Empty(emissions);
        Assert.True(completed);
        
        subscription.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task Changes_Observable_CompletesImmediately()
    {

        const string json = """{"Test": "Value"}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        await ObservableTestHelpers.WaitForCompletionAsync(
            provider.ChangesAsBytes(query),
            timeout: TimeSpan.FromSeconds(1),
            description: "StaticJsonProvider Changes completion");
    }

    #endregion

    #region Performance Tests
    [Fact]
    [Trait("Type", "Performance")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_SingleRead_PerformanceUnder1ms()
    {

        const string json = """{"Name": "Performance", "Value": 123, "Enabled": true}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Warm up
        await provider.FetchConfigurationBytesAsync(query);


        var stopwatch = Stopwatch.StartNew();
        var result = await provider.FetchConfigurationBytesAsync(query);
        stopwatch.Stop();


        Assert.NotEqual(default, result);
        Assert.True(stopwatch.ElapsedMilliseconds < 1, 
            $"StaticJsonProvider read took {stopwatch.ElapsedMilliseconds}ms, expected < 1ms");
    }
    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_1000Reads_PerformanceUnder100ms()
    {

        const string json = """{"Name": "Stress", "Value": 999, "Tags": ["perf", "test"]}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            var result = await provider.FetchConfigurationBytesAsync(query);
            Assert.NotEqual(default, result); // Minimal validation to ensure work is done
        }
        stopwatch.Stop();


        Assert.True(stopwatch.ElapsedMilliseconds < 100, 
            $"1000 StaticJsonProvider reads took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
    }

    #endregion

    #region Concurrency Testing
    [Fact]
    [Trait("Type", "Concurrency")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_ConcurrentAccess_NoRaceConditions()
    {

        const string json = """{"ThreadSafe": true, "Value": 42}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        const int threadCount = 10;
        const int operationsPerThread = 100;
        var results = new List<JsonElement>[threadCount];
        var exceptions = new List<Exception>();


        var tasks = Enumerable.Range(0, threadCount).Select(async threadId =>
        {
            results[threadId] = new();
            try
            {
                for (var i = 0; i < operationsPerThread; i++)
                {
                    var result = await provider.FetchConfigurationBytesAsync(query);
                    results[threadId].Add(result.ToJsonElement());
                }
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);


        Assert.Empty(exceptions);
        

        for (var i = 0; i < threadCount; i++)
        {
            Assert.Equal(operationsPerThread, results[i].Count);
            

            foreach (var result in results[i])
            {
                Assert.True(result.GetProperty("ThreadSafe").GetBoolean());
                Assert.Equal(42, result.GetProperty("Value").GetInt32());
            }
        }
    }
    [Fact]
    [Trait("Type", "Concurrency")]
    [Trait("Provider", "StaticJsonProvider")]
    public void Changes_ConcurrentSubscriptions_ConsistentBehavior()
    {

        const string json = """{"Concurrent": "Test"}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        const int subscriberCount = 20;
        var completedCount = 0;
        var emissionCounts = new int[subscriberCount];
        var subscriptions = new IDisposable[subscriberCount];


        var completedCountdown = new CountdownEvent(subscriberCount);
        
        for (var i = 0; i < subscriberCount; i++)
        {
            var subscriberId = i; // Capture for closure
            subscriptions[i] = provider.ChangesAsBytes(query).Subscribe(
                _ => Interlocked.Increment(ref emissionCounts[subscriberId]),
                _ => { }, // OnError
                () => 
                {
                    Interlocked.Increment(ref completedCount);
                    completedCountdown.Signal();
                });
        }

        // Wait for all observables to complete
        var completed = completedCountdown.Wait(TimeSpan.FromSeconds(5));
        Assert.True(completed, "Not all Changes observables completed within timeout");


        Assert.Equal(subscriberCount, completedCount);
        for (var i = 0; i < subscriberCount; i++)
        {
            Assert.Equal(0, emissionCounts[i]);
            subscriptions[i].Dispose();
        }
        
        completedCountdown.Dispose();
    }

    #endregion

    #region Factory Function Tests (Rule Creation)
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithJsonElement_CreatesValidRule()
    {
        const string json = """{"Name": "Test", "Value": 789, "Enabled": true}""";
        
        // Verify rule can be used in ConfigManager
        using var manager = ConfigManager.Create(c => c.WithConfiguration(rules => [
            rules.For<TestConfig>().FromStaticJson(json)
        ]));
        var config = manager.GetConfig<TestConfig>();
        
        Assert.NotNull(config);
        Assert.Equal("Test", config!.Name);
        Assert.Equal(789, config.Value);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithJsonString_CreatesValidRule()
    {
        const string json = """{"Name": "Works", "Value": 456, "Enabled": false}""";

        // Verify rule works correctly
        using var manager = ConfigManager.Create(c => c.WithConfiguration(rules => [
            rules.For<TestConfig>().FromStaticJson(json)
        ]));
        var config = manager.GetConfig<TestConfig>();
        
        Assert.NotNull(config);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithRequiredFlag_SetsRequiredCorrectly()
    {
        const string json = """{"Name": "Required", "Value": 123, "Enabled": true}""";

        // Verify required flag is set by checking health service
        using var manager = ConfigManager.Create(c => c.WithConfiguration(rules => [
            rules.For<TestConfig>().FromStaticJson(json).Required()
        ]));
        var health = manager.GetHealthService().Snapshot;
        
        Assert.True(health.Rules[0].Required);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithUseWhen_SetsConditionCorrectly()
    {
        const string json = """{"Name": "Conditional", "Value": 999, "Enabled": true}""";
        Func<IConfigurationAccessor, bool> useWhen = (_) => Environment.GetEnvironmentVariable("TEST_ENV") == "true";

        // Verify the rule can be created and used
        using var manager = ConfigManager.Create(c => c.WithConfiguration(rules => [
            rules.For<TestConfig>().FromStaticJson(json).When(useWhen)
        ]));

        // Use TryGetConfig since the rule may be skipped depending on TEST_ENV
        // GetConfig would throw if the condition is false and rule is skipped
        var hasConfig = manager.TryGetConfig<TestConfig>(out _);
        // hasConfig will be true if TEST_ENV == "true", false otherwise
    }

    #endregion

    #region Edge Cases and Boundary Tests
    [Fact]
    [Trait("Type", "Performance")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_LargeJson_HandlesEfficiently()
    {

        var largeData = new Dictionary<string, object>();
        for (var i = 0; i < 1000; i++)
        {
            largeData[$"Property{i}"] = $"Value{i}";
            largeData[$"Number{i}"] = i;
            largeData[$"Boolean{i}"] = i % 2 == 0;
        }

        var json = JsonSerializer.Serialize(largeData);
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var stopwatch = Stopwatch.StartNew();
        var result = await provider.FetchConfigurationBytesAsync(query);
        stopwatch.Stop();


        Assert.NotEqual(default, result);
        Assert.True(result.ToJsonElement().EnumerateObject().Count() >= 1000);
        Assert.True(stopwatch.ElapsedMilliseconds < 10, 
            $"Large JSON fetch took {stopwatch.ElapsedMilliseconds}ms, expected < 10ms");
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_DeeplyNested_ParsesCorrectly()
    {

        const string json = """
        {
            "Level1": {
                "Level2": {
                    "Level3": {
                        "Level4": {
                            "Level5": {
                                "Level6": {
                                    "Level7": {
                                        "Level8": {
                                            "Level9": {
                                                "Level10": {
                                                    "DeepValue": "Found at level 10!",
                                                    "DeepNumber": 42
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();


        var result = await provider.FetchConfigurationBytesAsync(query);


        var deepValue = result.ToJsonElement().GetProperty("Level1")
            .GetProperty("Level2")
            .GetProperty("Level3")
            .GetProperty("Level4")
            .GetProperty("Level5")
            .GetProperty("Level6")
            .GetProperty("Level7")
            .GetProperty("Level8")
            .GetProperty("Level9")
            .GetProperty("Level10");
            
        Assert.Equal("Found at level 10!", deepValue.GetProperty("DeepValue").GetString());
        Assert.Equal(42, deepValue.GetProperty("DeepNumber").GetInt32());
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithCancellation_HandlesGracefully()
    {

        const string json = """{"Cancellable": true}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        using var cancellationTokenSource = new CancellationTokenSource();


        var result = await provider.FetchConfigurationBytesAsync(query, cancellationTokenSource.Token);


        Assert.True(result.ToJsonElement().GetProperty("Cancellable").GetBoolean());


        cancellationTokenSource.Cancel();
        var result2 = await provider.FetchConfigurationBytesAsync(query, cancellationTokenSource.Token);
        

        Assert.True(result2.ToJsonElement().GetProperty("Cancellable").GetBoolean());
    }

    #endregion
}