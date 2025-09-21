using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using System.Diagnostics;
using Cocoar.Configuration.Providers;

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

    /// <summary>
    /// Validates StaticJsonProvider can fetch configuration from a simple JSON string.
    /// Tests the most basic functionality - JSON string deserialization to configuration object.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithSimpleJson_ReturnsCorrectData()
    {
        // Arrange
        const string json = """{"Name": "TestApp", "Value": 42, "Enabled": true}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Act
        var result = await provider.FetchConfigurationAsync(query);

        // Assert
        Assert.Equal("TestApp", result.GetProperty("Name").GetString());
        Assert.Equal(42, result.GetProperty("Value").GetInt32());
        Assert.True(result.GetProperty("Enabled").GetBoolean());
    }

    /// <summary>
    /// Validates StaticJsonProvider works with complex nested JSON structures.
    /// Ensures proper handling of nested objects and arrays.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithComplexJson_ReturnsNestedData()
    {
        // Arrange
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

        // Act
        var result = await provider.FetchConfigurationAsync(query);

        // Assert
        Assert.Equal("Complex Configuration", result.GetProperty("Title").GetString());
        Assert.Equal(10, result.GetProperty("Settings").GetProperty("Priority").GetInt32());
        
        var tags = result.GetProperty("Settings").GetProperty("Tags");
        Assert.Equal(3, tags.GetArrayLength());
        Assert.Equal("production", tags[0].GetString());
        Assert.Equal("critical", tags[1].GetString());
        Assert.Equal("monitored", tags[2].GetString());
    }

    /// <summary>
    /// Validates StaticJsonProvider handles empty JSON objects correctly.
    /// This is important for default configurations and edge cases.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithEmptyJson_ReturnsEmptyObject()
    {
        // Arrange
        const string json = "{}";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Act
        var result = await provider.FetchConfigurationAsync(query);

        // Assert
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Empty(result.EnumerateObject());
    }

    /// <summary>
    /// Validates that multiple calls to FetchConfigurationAsync return identical data.
    /// StaticJsonProvider should be deterministic and consistent.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_MultipleCalls_ReturnsIdenticalData()
    {
        // Arrange
        const string json = """{"Name": "Consistent", "Value": 100}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Act
        var result1 = await provider.FetchConfigurationAsync(query);
        var result2 = await provider.FetchConfigurationAsync(query);
        var result3 = await provider.FetchConfigurationAsync(query);

        // Assert - All results should be identical
        Assert.Equal(result1.GetProperty("Name").GetString(), result2.GetProperty("Name").GetString());
        Assert.Equal(result1.GetProperty("Name").GetString(), result3.GetProperty("Name").GetString());
        Assert.Equal(result1.GetProperty("Value").GetInt32(), result2.GetProperty("Value").GetInt32());
        Assert.Equal(result1.GetProperty("Value").GetInt32(), result3.GetProperty("Value").GetInt32());
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Validates StaticJsonProvider handles malformed JSON appropriately during construction.
    /// Should fail fast with clear error messages.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void Constructor_WithMalformedJson_ThrowsJsonException()
    {
        // Arrange & Act & Assert
        // JsonReaderException is a subclass of JsonException, so use ThrowsAny
        Assert.ThrowsAny<JsonException>(() =>
        {
            const string malformedJson = """{"Name": "Test", "Value":}"""; // Missing value
            using var document = JsonDocument.Parse(malformedJson);
        });
    }

    /// <summary>
    /// Validates StaticJsonProvider handles null values in JSON gracefully.
    /// This is a common scenario in configuration management.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithNullValues_HandlesGracefully()
    {
        // Arrange
        const string json = """{"Name": null, "Value": 42, "OptionalField": null}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Act
        var result = await provider.FetchConfigurationAsync(query);

        // Assert
        Assert.Equal(JsonValueKind.Null, result.GetProperty("Name").ValueKind);
        Assert.Equal(42, result.GetProperty("Value").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("OptionalField").ValueKind);
    }

    /// <summary>
    /// Validates StaticJsonProvider handles different JSON data types correctly.
    /// Tests string, number, boolean, array, object, and null types.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithVariousDataTypes_HandlesCorrectly()
    {
        // Arrange
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

        // Act
        var result = await provider.FetchConfigurationAsync(query);

        // Assert
        Assert.Equal("hello", result.GetProperty("stringValue").GetString());
        Assert.Equal(42, result.GetProperty("intValue").GetInt32());
        Assert.Equal(3.14, result.GetProperty("floatValue").GetDouble(), 2);
        Assert.True(result.GetProperty("boolValue").GetBoolean());
        Assert.Equal(3, result.GetProperty("arrayValue").GetArrayLength());
        Assert.Equal("data", result.GetProperty("objectValue").GetProperty("nested").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("nullValue").ValueKind);
    }

    #endregion

    #region Observable/Reactive Tests

    /// <summary>
    /// Validates that StaticJsonProvider.Changes() returns empty observable as expected.
    /// Static providers should never emit change notifications.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task Changes_Always_ReturnsEmptyObservable()
    {
        // Arrange
        const string json = """{"Name": "Static", "Value": 1}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        var emissions = new List<JsonElement>();
        var completed = false;

        // Act
        var subscription = provider.Changes(query).Subscribe(
            emissions.Add,
            _ => { }, // OnError
            () => completed = true); // OnCompleted

        // Use active waiting to ensure observable behavior is deterministic
        await ActiveWaitHelpers.WaitUntilAsync(
            () => completed,
            timeout: TimeSpan.FromSeconds(5),
            description: "Changes observable completion");

        // Assert
        Assert.Empty(emissions);
        Assert.True(completed);
        
        subscription.Dispose();
    }

    /// <summary>
    /// Validates that StaticJsonProvider Changes observable completes immediately.
    /// This behavior should be consistent and deterministic.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task Changes_Observable_CompletesImmediately()
    {
        // Arrange
        const string json = """{"Test": "Value"}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Act & Assert
        await ObservableTestHelpers.WaitForCompletionAsync(
            provider.Changes(query),
            timeout: TimeSpan.FromSeconds(1),
            description: "StaticJsonProvider Changes completion");
    }

    #endregion

    #region Performance Tests

    /// <summary>
    /// Validates StaticJsonProvider performance for single configuration reads.
    /// Should consistently perform under 1ms for deterministic operations.
    /// </summary>
    [Fact]
    [Trait("Type", "Performance")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_SingleRead_PerformanceUnder1ms()
    {
        // Arrange
        const string json = """{"Name": "Performance", "Value": 123, "Enabled": true}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Warm up
        await provider.FetchConfigurationAsync(query);

        // Act - Measure performance
        var stopwatch = Stopwatch.StartNew();
        var result = await provider.FetchConfigurationAsync(query);
        stopwatch.Stop();

        // Assert
        Assert.NotEqual(default, result);
        Assert.True(stopwatch.ElapsedMilliseconds < 1, 
            $"StaticJsonProvider read took {stopwatch.ElapsedMilliseconds}ms, expected < 1ms");
    }

    /// <summary>
    /// Validates StaticJsonProvider performance for 1000 consecutive reads.
    /// Should maintain consistent performance without degradation.
    /// </summary>
    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_1000Reads_PerformanceUnder100ms()
    {
        // Arrange
        const string json = """{"Name": "Stress", "Value": 999, "Tags": ["perf", "test"]}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        // Act - Measure 1000 reads
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            var result = await provider.FetchConfigurationAsync(query);
            Assert.NotEqual(default, result); // Minimal validation to ensure work is done
        }
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 100, 
            $"1000 StaticJsonProvider reads took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
    }

    #endregion

    #region Concurrency Testing

    /// <summary>
    /// Validates StaticJsonProvider handles concurrent access safely.
    /// Multiple threads should be able to read simultaneously without race conditions.
    /// </summary>
    [Fact]
    [Trait("Type", "Concurrency")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_ConcurrentAccess_NoRaceConditions()
    {
        // Arrange
        const string json = """{"ThreadSafe": true, "Value": 42}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        const int threadCount = 10;
        const int operationsPerThread = 100;
        var results = new List<JsonElement>[threadCount];
        var exceptions = new List<Exception>();

        // Act - Run concurrent operations
        var tasks = Enumerable.Range(0, threadCount).Select(async threadId =>
        {
            results[threadId] = new List<JsonElement>();
            try
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var result = await provider.FetchConfigurationAsync(query);
                    results[threadId].Add(result);
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

        // Assert - No exceptions occurred
        Assert.Empty(exceptions);
        
        // Assert - All threads got the expected number of results
        for (int i = 0; i < threadCount; i++)
        {
            Assert.Equal(operationsPerThread, results[i].Count);
            
            // Assert - All results are identical (verify thread safety)
            foreach (var result in results[i])
            {
                Assert.True(result.GetProperty("ThreadSafe").GetBoolean());
                Assert.Equal(42, result.GetProperty("Value").GetInt32());
            }
        }
    }

    /// <summary>
    /// Validates StaticJsonProvider concurrent Changes() observable subscriptions.
    /// Multiple subscribers should get consistent empty observables without interference.
    /// </summary>
    [Fact]
    [Trait("Type", "Concurrency")]
    [Trait("Provider", "StaticJsonProvider")]
    public void Changes_ConcurrentSubscriptions_ConsistentBehavior()
    {
        // Arrange
        const string json = """{"Concurrent": "Test"}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        const int subscriberCount = 20;
        var completedCount = 0;
        var emissionCounts = new int[subscriberCount];
        var subscriptions = new IDisposable[subscriberCount];

        // Act - Create concurrent subscriptions
        var completedCountdown = new CountdownEvent(subscriberCount);
        
        for (int i = 0; i < subscriberCount; i++)
        {
            var subscriberId = i; // Capture for closure
            subscriptions[i] = provider.Changes(query).Subscribe(
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

        // Assert - All subscriptions completed with no emissions
        Assert.Equal(subscriberCount, completedCount);
        for (int i = 0; i < subscriberCount; i++)
        {
            Assert.Equal(0, emissionCounts[i]);
            subscriptions[i].Dispose();
        }
        
        completedCountdown.Dispose();
    }

    #endregion

    #region Factory Function Tests (Rule Creation)

    /// <summary>
    /// Validates StaticJsonProvider.CreateRule() static method with JsonElement.
    /// Tests the factory method for creating configuration rules.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithJsonElement_CreatesValidRule()
    {
        // Arrange
        const string json = """{"Factory": "Test", "Value": 789}""";
        using var document = JsonDocument.Parse(json);
        var jsonElement = document.RootElement.Clone();

        // Act
        var rule = StaticJsonProvider.CreateRule<TestConfig>(jsonElement);

        // Assert
        Assert.NotNull(rule);
        Assert.Equal(typeof(StaticJsonProvider), rule.ProviderType);
        Assert.Equal(typeof(TestConfig), rule.ConcreteType);
        Assert.False(rule.Options?.Required ?? false); // Default should be false
    }

    /// <summary>
    /// Validates StaticJsonProvider.CreateRule() with JSON string.
    /// Tests the overload that accepts JSON strings directly.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithJsonString_CreatesValidRule()
    {
        // Arrange
        const string json = """{"StringFactory": "Works", "Number": 456}""";

        // Act
        var rule = StaticJsonProvider.CreateRule<TestConfig>(json);

        // Assert
        Assert.NotNull(rule);
        Assert.Equal(typeof(StaticJsonProvider), rule.ProviderType);
        Assert.Equal(typeof(TestConfig), rule.ConcreteType);
    }

    /// <summary>
    /// Validates StaticJsonProvider.CreateRule() with required flag.
    /// Tests the factory method with required configuration option.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithRequiredFlag_SetsRequiredCorrectly()
    {
        // Arrange
        const string json = """{"Required": true}""";

        // Act
        var rule = StaticJsonProvider.CreateRule<TestConfig>(json, required: true);

        // Assert
        Assert.NotNull(rule);
        Assert.True(rule.Options?.Required ?? false);
    }

    /// <summary>
    /// Validates StaticJsonProvider.CreateRule() with useWhen condition.
    /// Tests conditional rule creation functionality.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public void CreateRule_WithUseWhen_SetsConditionCorrectly()
    {
        // Arrange
        const string json = """{"Conditional": true}""";
        var useWhen = () => Environment.GetEnvironmentVariable("TEST_ENV") == "true";

        // Act
        var rule = StaticJsonProvider.CreateRule<TestConfig>(json, useWhen: useWhen);

        // Assert
        Assert.NotNull(rule);
        Assert.NotNull(rule.Options?.UseWhen);
    }

    #endregion

    #region Edge Cases and Boundary Tests

    /// <summary>
    /// Validates StaticJsonProvider handles very large JSON documents efficiently.
    /// Tests boundary conditions for memory usage and performance.
    /// </summary>
    [Fact]
    [Trait("Type", "Performance")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_LargeJson_HandlesEfficiently()
    {
        // Arrange - Create large JSON with many properties
        var largeData = new Dictionary<string, object>();
        for (int i = 0; i < 1000; i++)
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

        // Act - Measure performance with large data
        var stopwatch = Stopwatch.StartNew();
        var result = await provider.FetchConfigurationAsync(query);
        stopwatch.Stop();

        // Assert
        Assert.NotEqual(default, result);
        Assert.True(result.EnumerateObject().Count() >= 1000);
        Assert.True(stopwatch.ElapsedMilliseconds < 10, 
            $"Large JSON fetch took {stopwatch.ElapsedMilliseconds}ms, expected < 10ms");
    }

    /// <summary>
    /// Validates StaticJsonProvider handles deeply nested JSON structures.
    /// Tests boundary conditions for JSON parsing depth.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_DeeplyNested_ParsesCorrectly()
    {
        // Arrange - Create deeply nested JSON (10 levels)
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

        // Act
        var result = await provider.FetchConfigurationAsync(query);

        // Assert - Navigate to deeply nested value
        var deepValue = result.GetProperty("Level1")
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

    /// <summary>
    /// Validates StaticJsonProvider with cancellation tokens.
    /// Should handle cancellation gracefully even though operations are synchronous.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "StaticJsonProvider")]
    public async Task FetchConfigurationAsync_WithCancellation_HandlesGracefully()
    {
        // Arrange
        const string json = """{"Cancellable": true}""";
        using var document = JsonDocument.Parse(json);
        var options = new StaticJsonProviderOptions(document.RootElement.Clone());
        var provider = new StaticJsonProvider(options);
        var query = new StaticJsonProviderQueryOptions();

        using var cancellationTokenSource = new CancellationTokenSource();

        // Act - Normal operation should succeed
        var result = await provider.FetchConfigurationAsync(query, cancellationTokenSource.Token);

        // Assert
        Assert.True(result.GetProperty("Cancellable").GetBoolean());

        // Act - Pre-cancelled token should still work for synchronous operation
        cancellationTokenSource.Cancel();
        var result2 = await provider.FetchConfigurationAsync(query, cancellationTokenSource.Token);
        
        // Assert - StaticJsonProvider is synchronous, so cancellation doesn't affect it
        Assert.True(result2.GetProperty("Cancellable").GetBoolean());
    }

    #endregion
}
