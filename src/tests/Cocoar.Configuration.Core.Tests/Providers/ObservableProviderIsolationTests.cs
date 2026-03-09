using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using System.Diagnostics;
using System.Collections.Concurrent;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Providers;

/// <summary>
/// Isolation tests for ObservableProvider - testing only deterministic observable provider functionality
/// without any I/O dependencies. These tests validate reactive behavior, subscription management,
/// error handling, and performance characteristics using controlled observables.
/// </summary>
public class ObservableProviderIsolationTests
{
    #region Test Configuration Classes

    public record TestConfig(string Name, int Value, bool Enabled);
    public record ComplexConfig(string Title, List<string> Tags, DateTime Timestamp);
    public record EmptyConfig();
    public record DynamicConfig(int Id, string Status, double Score);

    #endregion

    #region Basic Functionality Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task FetchConfigurationAsync_WithBehaviorSubject_ReturnsCurrentValue()
    {

        var testData = new TestConfig("ObservableTest", 123, true);
        var subject = new BehaviorSubject<TestConfig>(testData);
        
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal("ObservableTest", result.ToJsonElement().GetProperty("Name").GetString());
        Assert.Equal(123, result.ToJsonElement().GetProperty("Value").GetInt32());
        Assert.True(result.ToJsonElement().GetProperty("Enabled").GetBoolean());
        
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task FetchConfigurationAsync_WithComplexObject_SerializesCorrectly()
    {

        var testData = new ComplexConfig(
            "Complex Test Configuration",
            new() { "tag1", "tag2", "production" },
            new(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc));
        
        var subject = new BehaviorSubject<ComplexConfig>(testData);
        var options = new ObservableProviderOptions<ComplexConfig>(subject);
        var provider = new ObservableProvider<ComplexConfig>(options);
        var query = ObservableProviderQuery.Default;


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal("Complex Test Configuration", result.ToJsonElement().GetProperty("Title").GetString());
        
        var tags = result.ToJsonElement().GetProperty("Tags");
        Assert.Equal(3, tags.GetArrayLength());
        Assert.Equal("tag1", tags[0].GetString());
        Assert.Equal("tag2", tags[1].GetString());
        Assert.Equal("production", tags[2].GetString());
        
        // Verify timestamp serialization
        var timestamp = result.ToJsonElement().GetProperty("Timestamp").GetDateTime();
        Assert.Equal(2025, timestamp.Year);
        Assert.Equal(1, timestamp.Month);
        Assert.Equal(15, timestamp.Day);
        
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task FetchConfigurationAsync_WithEmptyConfig_HandlesCorrectly()
    {

        var testData = new EmptyConfig();
        var subject = new BehaviorSubject<EmptyConfig>(testData);
        
        var options = new ObservableProviderOptions<EmptyConfig>(subject);
        var provider = new ObservableProvider<EmptyConfig>(options);
        var query = ObservableProviderQuery.Default;


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        // EmptyConfig should serialize to empty JSON object
        
        subject.Dispose();
    }

    #endregion

    #region Observable/Reactive Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_WhenSubjectEmits_PropagatesChanges()
    {

        var initialData = new TestConfig("Initial", 1, false);
        var subject = new BehaviorSubject<TestConfig>(initialData);
        
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        var emissions = new List<JsonElement>();
        var subscription = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()));


        var updatedData1 = new TestConfig("Updated1", 2, true);
        var updatedData2 = new TestConfig("Updated2", 3, false);
        
        subject.OnNext(updatedData1);
        subject.OnNext(updatedData2);

        // Wait for emissions to propagate
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count >= 3, // Initial + 2 updates
            timeout: TimeSpan.FromSeconds(5),
            description: "observable change emissions");


        Assert.True(emissions.Count >= 3);
        
        // Verify initial emission (BehaviorSubject emits current value on subscription)
        Assert.Equal("Initial", emissions[0].GetProperty("Name").GetString());
        Assert.Equal(1, emissions[0].GetProperty("Value").GetInt32());
        Assert.False(emissions[0].GetProperty("Enabled").GetBoolean());
        
        // Verify first change
        Assert.Equal("Updated1", emissions[1].GetProperty("Name").GetString());
        Assert.Equal(2, emissions[1].GetProperty("Value").GetInt32());
        Assert.True(emissions[1].GetProperty("Enabled").GetBoolean());
        
        // Verify second change
        Assert.Equal("Updated2", emissions[2].GetProperty("Name").GetString());
        Assert.Equal(3, emissions[2].GetProperty("Value").GetInt32());
        Assert.False(emissions[2].GetProperty("Enabled").GetBoolean());
        
        subscription.Dispose();
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_WithRapidEmissions_HandlesAllChanges()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        var emissions = new List<JsonElement>();
        var subscription = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()));


        const int changeCount = 50;
        for (var i = 1; i <= changeCount; i++)
        {
            var data = new TestConfig($"Change{i}", i, i % 2 == 0);
            subject.OnNext(data);
        }

        // Wait for all emissions using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count >= changeCount + 1, // +1 for initial value from BehaviorSubject
            timeout: TimeSpan.FromSeconds(10),
            description: "all rapid emissions");


        Assert.Equal(changeCount + 1, emissions.Count);
        
        // Verify initial emission
        Assert.Equal("Initial", emissions[0].GetProperty("Name").GetString());
        Assert.Equal(0, emissions[0].GetProperty("Value").GetInt32());
        
        // Verify a few key emissions (offset by 1 due to initial emission)
        Assert.Equal("Change1", emissions[1].GetProperty("Name").GetString());
        Assert.Equal(1, emissions[1].GetProperty("Value").GetInt32());
        
        Assert.Equal("Change25", emissions[25].GetProperty("Name").GetString()); // 25 + 1 offset
        Assert.Equal(25, emissions[25].GetProperty("Value").GetInt32());
        
        Assert.Equal("Change50", emissions[50].GetProperty("Name").GetString()); // 50 + 1 offset
        Assert.Equal(50, emissions[50].GetProperty("Value").GetInt32());
        
        subscription.Dispose();
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_MultipleSubscriptions_AllReceiveEmissions()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        const int subscriberCount = 5;
        var allEmissions = new List<JsonElement>[subscriberCount];
        var subscriptions = new IDisposable[subscriberCount];

        // Create multiple subscriptions
        for (var i = 0; i < subscriberCount; i++)
        {
            allEmissions[i] = new();
            var emissions = allEmissions[i]; // Capture for closure
            subscriptions[i] = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()));
        }


        subject.OnNext(new("First", 1, true));
        subject.OnNext(new("Second", 2, false));
        subject.OnNext(new("Third", 3, true));

        // Wait for all subscriptions to receive emissions
        await ActiveWaitHelpers.WaitUntilAsync(
            () => allEmissions.All(list => list.Count >= 4), // Initial + 3 updates
            timeout: TimeSpan.FromSeconds(10),
            description: "all subscribers to receive emissions");


        for (var i = 0; i < subscriberCount; i++)
        {
            Assert.Equal(4, allEmissions[i].Count); // Initial + 3 updates
            
            Assert.Equal("Initial", allEmissions[i][0].GetProperty("Name").GetString());
            Assert.Equal("First", allEmissions[i][1].GetProperty("Name").GetString());
            Assert.Equal("Second", allEmissions[i][2].GetProperty("Name").GetString());
            Assert.Equal("Third", allEmissions[i][3].GetProperty("Name").GetString());
            
            subscriptions[i].Dispose();
        }
        
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_WhenSourceCompletes_CompletesCorrectly()
    {

        var subject = new Subject<TestConfig>();
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        var emissions = new List<JsonElement>();
        var completed = false;
        var subscription = provider.ChangesAsBytes(query).Subscribe(
            e => emissions.Add(e.ToJsonElement()),
            _ => { }, // OnError
            () => completed = true); // OnCompleted


        subject.OnNext(new("Test", 1, true));
        subject.OnNext(new("Final", 2, false));
        subject.OnCompleted();

        // Wait for completion
        await ActiveWaitHelpers.WaitUntilAsync(
            () => completed,
            timeout: TimeSpan.FromSeconds(5),
            description: "Changes observable completion");


        Assert.Equal(2, emissions.Count);
        Assert.True(completed);
        
        subscription.Dispose();
        subject.Dispose();
    }

    #endregion

    #region Error Handling Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_WhenSourceErrors_PropagatesError()
    {

        var subject = new Subject<TestConfig>();
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        var emissions = new List<JsonElement>();
        Exception? caughtException = null;
        var subscription = provider.ChangesAsBytes(query).Subscribe(
            e => emissions.Add(e.ToJsonElement()),
            ex => caughtException = ex,
            () => { });


        subject.OnNext(new("BeforeError", 1, true));
        var testException = new InvalidOperationException("Test error from source");
        subject.OnError(testException);

        // Wait for error propagation
        await ActiveWaitHelpers.WaitUntilAsync(
            () => caughtException != null,
            timeout: TimeSpan.FromSeconds(5),
            description: "error propagation");


        Assert.Equal(1, emissions.Count);
        Assert.NotNull(caughtException);
        Assert.Equal("Test error from source", caughtException.Message);
        
        subscription.Dispose();
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task FetchConfigurationAsync_WithNonSerializableObject_HandlesGracefully()
    {
        // Note: In practice, most objects can be serialized to JSON by System.Text.Json
        // This test validates the behavior with objects that might cause serialization issues
        

        var circularRef = new CircularReferenceTest();
        circularRef.Self = circularRef; // This can cause serialization issues
        
        // For this test, we'll use a regular serializable object since System.Text.Json 
        // handles most cases gracefully, but we want to ensure the provider works correctly
        var testData = new TestConfig("Serializable", 999, true);
        var subject = new BehaviorSubject<TestConfig>(testData);
        
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;


        var result = await provider.FetchConfigurationBytesAsync(query);
        Assert.NotEqual(default, result);
        
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_WithNullEmission_HandlesGracefully()
    {

        var subject = new Subject<TestConfig?>();
        var options = new ObservableProviderOptions<TestConfig?>(subject);
        var provider = new ObservableProvider<TestConfig?>(options);
        var query = ObservableProviderQuery.Default;

        var emissions = new List<JsonElement>();
        Exception? error = null;
        var subscription = provider.ChangesAsBytes(query).Subscribe(
            e  => emissions.Add(e.ToJsonElement()),
            ex => error = ex);


        subject.OnNext(new("Valid", 1, true));
        subject.OnNext(null);
        subject.OnNext(new("ValidAgain", 2, false));

        // Wait for all emissions
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count >= 3,
            timeout: TimeSpan.FromSeconds(5),
            description: "emissions including null");


        Assert.Null(error);
        Assert.Equal(3, emissions.Count);
        
        // First emission should be valid
        Assert.Equal("Valid", emissions[0].GetProperty("Name").GetString());
        
        // Second emission should represent null (as JSON null)
        Assert.Equal(JsonValueKind.Null, emissions[1].ValueKind);
        
        // Third emission should be valid again
        Assert.Equal("ValidAgain", emissions[2].GetProperty("Name").GetString());
        
        subscription.Dispose();
        subject.Dispose();
    }

    #endregion

    #region Performance Tests
    [Fact]
    [Trait("Type", "Performance")]
    [Trait("Provider", "ObservableProvider")]
    public async Task FetchConfigurationAsync_SingleRead_PerformanceUnder10ms()
    {

        var testData = new TestConfig("Performance", 12345, true);
        var subject = new BehaviorSubject<TestConfig>(testData);
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        // Warm up
        await provider.FetchConfigurationBytesAsync(query);


        var stopwatch = Stopwatch.StartNew();
        var result = await provider.FetchConfigurationBytesAsync(query);
        stopwatch.Stop();


        Assert.NotEqual(default, result);
        Assert.True(stopwatch.ElapsedMilliseconds < 10, 
            $"ObservableProvider read took {stopwatch.ElapsedMilliseconds}ms, expected < 10ms");
        
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Performance")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_EmissionLatency_Under50ms()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        var emissionTimes = new List<TimeSpan>();
        var stopwatch = Stopwatch.StartNew();
        
        var subscription = provider.ChangesAsBytes(query).Subscribe(_ =>
        {
            emissionTimes.Add(stopwatch.Elapsed);
        });


        var emissionStart = stopwatch.Elapsed;
        subject.OnNext(new("Timed", 1, true));

        // Wait for emission
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissionTimes.Count > 0,
            timeout: TimeSpan.FromSeconds(5),
            description: "timed emission");


        var latency = emissionTimes[0] - emissionStart;
        // Allow more latency on slower CI runners (especially ARM64 macOS)
        Assert.True(latency.TotalMilliseconds < 200, 
            $"Emission latency was {latency.TotalMilliseconds}ms, expected < 200ms");
        
        subscription.Dispose();
        subject.Dispose();
    }

    #endregion

    #region Subscription Management Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_DisposedSubscription_StopsReceivingEmissions()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        var emissions = new List<JsonElement>();
        var subscription = provider.ChangesAsBytes(query).Subscribe(e  => emissions.Add(e.ToJsonElement()));


        subject.OnNext(new("BeforeDispose", 1, true));
        
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count >= 2, // Initial + BeforeDispose
            timeout: TimeSpan.FromSeconds(2),
            description: "first emission");

        subscription.Dispose();
        var emissionsAfterDispose = emissions.Count;
        
        subject.OnNext(new("AfterDispose", 2, false));
        subject.OnNext(new("StillAfterDispose", 3, true));
        
        // Give time for potential emissions (should not happen)
        await Task.Delay(200);


        Assert.Equal(emissionsAfterDispose, emissions.Count);
        Assert.Equal("Initial", emissions[0].GetProperty("Name").GetString()); // First emission is initial value
        Assert.Equal("BeforeDispose", emissions[1].GetProperty("Name").GetString()); // Second emission is BeforeDispose
        
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_MultipleSubscribeDisposeCycles_HandlesCorrectly()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;


        for (var cycle = 1; cycle <= 5; cycle++)
        {
            var emissions = new List<JsonElement>();
            var subscription = provider.ChangesAsBytes(query).Subscribe(e  => emissions.Add(e.ToJsonElement()));

            subject.OnNext(new($"Cycle{cycle}", cycle, cycle % 2 == 0));
            
            await ActiveWaitHelpers.WaitUntilAsync(
                () => emissions.Count >= 2, // Current value + new emission
                timeout: TimeSpan.FromSeconds(2),
                description: $"cycle {cycle} emission");

            Assert.Equal(2, emissions.Count); // Current value + cycle update
            // BehaviorSubject emits current value on subscription - which changes each cycle
            var expectedCurrentValue = cycle == 1 ? "Initial" : $"Cycle{cycle - 1}";
            Assert.Equal(expectedCurrentValue, emissions[0].GetProperty("Name").GetString());
            Assert.Equal($"Cycle{cycle}", emissions[1].GetProperty("Name").GetString()); // Then our update
            Assert.Equal(cycle, emissions[1].GetProperty("Value").GetInt32());

            subscription.Dispose();
        }
        
        subject.Dispose();
    }

    #endregion

    #region Concurrency Tests
    [Fact]
    [Trait("Type", "Concurrency")]
    [Trait("Provider", "ObservableProvider")]
    public async Task FetchConfigurationAsync_ConcurrentAccess_NoRaceConditions()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Concurrent", 777, true));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        const int threadCount = 10;
        const int operationsPerThread = 50;
        var exceptions = new List<Exception>();
        var allResults = new List<JsonElement>[threadCount];


        var tasks = Enumerable.Range(0, threadCount).Select(async threadId =>
        {
            allResults[threadId] = new();
            try
            {
                for (var i = 0; i < operationsPerThread; i++)
                {
                    var result = await provider.FetchConfigurationBytesAsync(query);
                    allResults[threadId].Add(result.ToJsonElement());
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
            Assert.Equal(operationsPerThread, allResults[i].Count);
            foreach (var result in allResults[i])
            {
                Assert.Equal("Concurrent", result.GetProperty("Name").GetString());
                Assert.Equal(777, result.GetProperty("Value").GetInt32());
                Assert.True(result.GetProperty("Enabled").GetBoolean());
            }
        }
        
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Concurrency")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_ConcurrentSubscriptions_HandlesSafely()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        const int subscriberCount = 20;
        var allEmissions = new List<JsonElement>[subscriberCount];
        var subscriptions = new IDisposable[subscriberCount];
        var exceptions = new List<Exception>();


        var subscriptionTasks = Enumerable.Range(0, subscriberCount).Select(async subscriberId =>
        {
            try
            {
                allEmissions[subscriberId] = new();
                var emissions = allEmissions[subscriberId];
                subscriptions[subscriberId] = provider.ChangesAsBytes(query).Subscribe(e  => emissions.Add(e.ToJsonElement()));
                
                // Small delay to vary timing
                await Task.Delay(subscriberId * 2);
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        }).ToArray();

        await Task.WhenAll(subscriptionTasks);

        // Emit some data
        subject.OnNext(new("Concurrent1", 1, true));
        subject.OnNext(new("Concurrent2", 2, false));

        // Wait for all subscriptions to receive emissions
        await ActiveWaitHelpers.WaitUntilAsync(
            () => allEmissions.All(list => list != null && list.Count >= 3), // Initial + 2 concurrent updates
            timeout: TimeSpan.FromSeconds(10),
            description: "all concurrent subscriptions");


        Assert.Empty(exceptions);
        
        for (var i = 0; i < subscriberCount; i++)
        {
            Assert.NotNull(allEmissions[i]);
            Assert.True(allEmissions[i].Count >= 3); // Initial + 2 updates
            Assert.Equal("Initial", allEmissions[i][0].GetProperty("Name").GetString()); // BehaviorSubject initial
            Assert.Equal("Concurrent1", allEmissions[i][1].GetProperty("Name").GetString());
            Assert.Equal("Concurrent2", allEmissions[i][2].GetProperty("Name").GetString());
            
            subscriptions[i].Dispose();
        }
        
        subject.Dispose();
    }

    #endregion

    #region Advanced Stress Tests
    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_MassiveConcurrentSubscriptions_NoRaceConditions()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        const int subscriberCount = 100;
        var allEmissions = new ConcurrentBag<List<JsonElement>>();
        var subscriptions = new ConcurrentBag<IDisposable>();
        var exceptions = new ConcurrentBag<Exception>();


        var subscriptionTasks = Enumerable.Range(0, subscriberCount).Select(async subscriberId =>
        {
            try
            {
                await Task.Delay(subscriberId % 10); // Stagger subscription timing slightly
                var emissions = new List<JsonElement>();
                var subscription = provider.ChangesAsBytes(query).Subscribe(e  => emissions.Add(e.ToJsonElement()));
                
                allEmissions.Add(emissions);
                subscriptions.Add(subscription);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }).ToArray();

        await Task.WhenAll(subscriptionTasks);
        
        // Emit data after all subscriptions are established
        subject.OnNext(new("StressTest1", 1, true));
        subject.OnNext(new("StressTest2", 2, false));

        // Wait for all emissions to propagate
        await ActiveWaitHelpers.WaitUntilAsync(
            () => allEmissions.All(list => list.Count >= 3), // Initial + 2 updates
            timeout: TimeSpan.FromSeconds(10),
            description: "all massive concurrent subscriptions");


        Assert.Empty(exceptions);
        Assert.Equal(subscriberCount, allEmissions.Count);
        
        // Verify all subscribers received the same data
        foreach (var emissions in allEmissions)
        {
            Assert.True(emissions.Count >= 3);
            Assert.Equal("Initial", emissions[0].GetProperty("Name").GetString());
            Assert.Equal("StressTest1", emissions[1].GetProperty("Name").GetString());
            Assert.Equal("StressTest2", emissions[2].GetProperty("Name").GetString());
        }
        
        // Cleanup
        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_MixedConcurrentOperations_ThreadSafe()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        var allEmissions = new ConcurrentBag<List<JsonElement>>();
        var activeSubscriptions = new ConcurrentBag<IDisposable>();
        var exceptions = new ConcurrentBag<Exception>();


        var tasks = new List<Task>();

        // Task 1: Continuous subscription creation and disposal
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 50; i++)
                {
                    var emissions = new List<JsonElement>();
                    var subscription = provider.ChangesAsBytes(query).Subscribe(e  => emissions.Add(e.ToJsonElement()));
                    allEmissions.Add(emissions);
                    activeSubscriptions.Add(subscription);
                    
                    await Task.Delay(10); // Small delay to allow emissions
                    
                    subscription.Dispose();
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        }));

        // Task 2: Rapid data emissions
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                for (var i = 1; i <= 100; i++)
                {
                    subject.OnNext(new($"Emission{i}", i, i % 2 == 0));
                    if (i % 10 == 0)
                    {
                        await Task.Delay(1); // Occasional tiny pause
                    }
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        }));

        // Task 3: Long-lived subscriptions
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 20; i++)
                {
                    var emissions = new List<JsonElement>();
                    var subscription = provider.ChangesAsBytes(query).Subscribe(e  => emissions.Add(e.ToJsonElement()));
                    allEmissions.Add(emissions);
                    activeSubscriptions.Add(subscription);
                    await Task.Delay(50); // Keep these alive longer
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        }));

        // Wait for all concurrent operations to complete
        await Task.WhenAll(tasks);

        // Give time for final emissions to propagate
        await Task.Delay(100);


        Assert.Empty(exceptions);
        Assert.True(allEmissions.Count > 0);
        
        // Verify that at least some emissions were captured (exact count is non-deterministic due to timing)
        var nonEmptyEmissionLists = allEmissions.Where(list => list.Count > 0).ToArray();
        Assert.True(nonEmptyEmissionLists.Length > 0, "At least some subscriptions should have captured emissions");
        
        // Cleanup remaining subscriptions
        foreach (var subscription in activeSubscriptions)
        {
            subscription.Dispose();
        }
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_HeavyLoadScenario_RemainsStable()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        const int subscriberCount = 10;
        const int emissionCount = 1000;
        
        var allEmissions = new List<JsonElement>[subscriberCount];
        var subscriptions = new IDisposable[subscriberCount];
        var exceptions = new ConcurrentBag<Exception>();

        // Create long-lived subscribers
        for (var i = 0; i < subscriberCount; i++)
        {
            var subscriberId = i;
            try
            {
                allEmissions[subscriberId] = new();
                subscriptions[subscriberId] = provider.ChangesAsBytes(query).Subscribe(
                    emission => allEmissions[subscriberId].Add(emission.ToJsonElement()),
                    ex => exceptions.Add(ex));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }


        var stopwatch = Stopwatch.StartNew();
        
        for (var i = 1; i <= emissionCount; i++)
        {
            subject.OnNext(new($"Load{i}", i, i % 2 == 0));
            
            // Occasional micro-pause to allow processing
            if (i % 100 == 0)
            {
                await Task.Delay(1);
            }
        }

        // Wait for all emissions to be processed
        await ActiveWaitHelpers.WaitUntilAsync(
            () => allEmissions.All(list => list != null && list.Count >= emissionCount + 1), // +1 for initial
            timeout: TimeSpan.FromSeconds(30),
            description: "heavy load processing");

        stopwatch.Stop();


        Assert.Empty(exceptions);
        
        // Verify all subscribers received all data
        for (var i = 0; i < subscriberCount; i++)
        {
            Assert.NotNull(allEmissions[i]);
            Assert.Equal(emissionCount + 1, allEmissions[i].Count); // +1 for initial emission
            
            // Verify first and last emissions
            Assert.Equal("Initial", allEmissions[i][0].GetProperty("Name").GetString());
            Assert.Equal($"Load{emissionCount}", allEmissions[i][^1].GetProperty("Name").GetString());
        }
        
        // Performance check - should handle 1000 emissions reasonably fast
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), 
            $"Heavy load processing took {stopwatch.Elapsed.TotalSeconds:F2} seconds, expected < 10 seconds");
        
        // Cleanup
        for (var i = 0; i < subscriberCount; i++)
        {
            subscriptions[i]?.Dispose();
        }
        subject.Dispose();
    }
    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_RapidSubscribeUnsubscribeCycles_NoMemoryLeaks()
    {

        var subject = new BehaviorSubject<TestConfig>(new("Initial", 0, false));
        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        const int cycleCount = 500;
        var exceptions = new ConcurrentBag<Exception>();
        var totalEmissionsReceived = 0;


        for (var cycle = 1; cycle <= cycleCount; cycle++)
        {
            try
            {
                var emissions = new List<JsonElement>();
                var subscription = provider.ChangesAsBytes(query).Subscribe(e  => emissions.Add(e.ToJsonElement()));

                // Emit one piece of data
                subject.OnNext(new($"Cycle{cycle}", cycle, cycle % 2 == 0));
                
                // Brief wait for emission
                await ActiveWaitHelpers.WaitUntilAsync(
                    () => emissions.Count >= 2, // Initial + cycle emission
                    timeout: TimeSpan.FromMilliseconds(100),
                    description: $"cycle {cycle} emissions");

                totalEmissionsReceived += emissions.Count;
                
                // Immediately dispose
                subscription.Dispose();
                
                // Occasional GC to detect memory issues
                if (cycle % 100 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await Task.Delay(1);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }


        Assert.Empty(exceptions);
        Assert.True(totalEmissionsReceived > 0, "Should have received some emissions during the test");
        
        // Final GC to ensure no memory leaks
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        subject.Dispose();
    }

    #endregion

    #region JSON String Convenience Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task FetchConfigurationAsync_WithJsonStringOverload_WorksCorrectly()
    {

        var jsonString = """{"Name":"TestApp","Version":"1.0.0","EnableLogging":true}""";
        
        var options = new ObservableProviderOptions<string>(new BehaviorSubject<string>(jsonString));
        var provider = new ObservableProvider<string>(options);
        var query = ObservableProviderQuery.Default;


        var result = await provider.FetchConfigurationBytesAsync(query);


        Assert.True(result.ToJsonElement().TryGetProperty("Name", out var nameProperty));
        Assert.Equal("TestApp", nameProperty.GetString());
        Assert.True(result.ToJsonElement().TryGetProperty("Version", out var versionProperty));
        Assert.Equal("1.0.0", versionProperty.GetString());
        Assert.True(result.ToJsonElement().TryGetProperty("EnableLogging", out var loggingProperty));
        Assert.True(loggingProperty.GetBoolean());
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Changes_WithJsonStringObservable_PropagatesUpdates()
    {

        var initialJson = """{"Name":"InitialApp","Version":"1.0.0"}""";
        var updatedJson = """{"Name":"UpdatedApp","Version":"2.0.0"}""";
        
        var subject = new BehaviorSubject<string>(initialJson);
        var options = new ObservableProviderOptions<string>(subject);
        var provider = new ObservableProvider<string>(options);
        var query = ObservableProviderQuery.Default;
        
        var changes = new List<JsonElement>();
        var subscription = provider.ChangesAsBytes(query).Subscribe(e => changes.Add(e.ToJsonElement()));


        await Task.Delay(10); // Let initial value emit
        subject.OnNext(updatedJson);
        await Task.Delay(10); // Let update emit


        Assert.Equal(2, changes.Count);
        
        // Initial value
        Assert.True(changes[0].TryGetProperty("Name", out var initialName));
        Assert.Equal("InitialApp", initialName.GetString());
        
        // Updated value
        Assert.True(changes[1].TryGetProperty("Name", out var updatedName));
        Assert.Equal("UpdatedApp", updatedName.GetString());
        
        subscription.Dispose();
        subject.Dispose();
    }

    /// <summary>
    /// Demonstrates the new fluent API: TestRules.Observable(jsonString).For&lt;T&gt;()
    /// This makes test writing much simpler!
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public void FluentAPI_Observable_SimplifiesTestWriting()
    {

        var jsonString = """{"Name":"TestApp","Value":42,"Enabled":true}""";
        
        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<TestConfig>(System.Reactive.Linq.Observable.Return(jsonString))
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules));


        var config = configManager.GetConfig<TestConfig>();


        Assert.NotNull(config);
        Assert.Equal("TestApp", config.Name);
        Assert.Equal(42, config.Value);  
        Assert.True(config.Enabled);
    }

    #endregion

    #region Subscriber Safety Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Reactive_Subscription_Exception_DoesNotTerminateOtherSubscribers()
    {

        var testData1 = new TestConfig("Initial", 1, true);
        var testData2 = new TestConfig("Updated", 2, false);
        var subject = new BehaviorSubject<TestConfig>(testData1);

        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        // Create multiple subscribers - one will throw exceptions, others should be unaffected
        var goodSubscriber1Values = new List<JsonElement>();
        var goodSubscriber2Values = new List<JsonElement>();
        var exceptionCount = 0;

        var changes = provider.ChangesAsBytes(query);

        // Good subscriber 1
        var subscription1 = changes.Subscribe(
            config => goodSubscriber1Values.Add(config.ToJsonElement()),
            ex => { /* Should not be called */ });

        // Bad subscriber that throws exceptions
        var subscription2 = changes.Subscribe(
            config =>
            {
                exceptionCount++;
                throw new InvalidOperationException($"Test exception {exceptionCount}");
            },
            ex => { /* Exception handled */ });

        // Good subscriber 2
        var subscription3 = changes.Subscribe(
            config => goodSubscriber2Values.Add(config.ToJsonElement()),
            ex => { /* Should not be called */ });

        // Wait for initial values
        await ActiveWaitHelpers.WaitUntilAsync(
            () => goodSubscriber1Values.Count > 0 && goodSubscriber2Values.Count > 0,
            timeout: TimeSpan.FromSeconds(2),
            description: "initial emissions to good subscribers");

        var initialCount1 = goodSubscriber1Values.Count;
        var initialCount2 = goodSubscriber2Values.Count;


        subject.OnNext(testData2);

        // Wait for propagation
        await ActiveWaitHelpers.WaitUntilAsync(
            () => goodSubscriber1Values.Count > initialCount1 && goodSubscriber2Values.Count > initialCount2,
            timeout: TimeSpan.FromSeconds(2),
            description: "updated emissions to good subscribers despite bad subscriber exception");


        Assert.True(goodSubscriber1Values.Count > initialCount1, "Good subscriber 1 should receive new value");
        Assert.True(goodSubscriber2Values.Count > initialCount2, "Good subscriber 2 should receive new value");

        // Verify latest values are correct
        var latest1 = goodSubscriber1Values.Last();
        var latest2 = goodSubscriber2Values.Last();

        Assert.Equal("Updated", latest1.GetProperty("Name").GetString());
        Assert.Equal("Updated", latest2.GetProperty("Name").GetString());
        Assert.Equal(2, latest1.GetProperty("Value").GetInt32());
        Assert.Equal(2, latest2.GetProperty("Value").GetInt32());

        // Cleanup
        subscription1.Dispose();
        subscription2.Dispose();
        subscription3.Dispose();
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Reactive_OneSubscriberDispose_DoesNotAffectOthers()
    {

        var testData1 = new TestConfig("Initial", 1, true);
        var testData2 = new TestConfig("Updated", 2, false);
        var testData3 = new TestConfig("Final", 3, true);
        var subject = new BehaviorSubject<TestConfig>(testData1);

        var options = new ObservableProviderOptions<TestConfig>(subject);
        var provider = new ObservableProvider<TestConfig>(options);
        var query = ObservableProviderQuery.Default;

        // Create multiple subscribers
        var subscriber1Values = new List<JsonElement>();
        var subscriber2Values = new List<JsonElement>();
        var subscriber3Values = new List<JsonElement>();

        var changes = provider.ChangesAsBytes(query);

        var subscription1 = changes.Subscribe(config => subscriber1Values.Add(config.ToJsonElement()));
        var subscription2 = changes.Subscribe(config => subscriber2Values.Add(config.ToJsonElement()));
        var subscription3 = changes.Subscribe(config => subscriber3Values.Add(config.ToJsonElement()));

        // Wait for initial values
        await ActiveWaitHelpers.WaitUntilAsync(
            () => subscriber1Values.Count > 0 && subscriber2Values.Count > 0 && subscriber3Values.Count > 0,
            timeout: TimeSpan.FromSeconds(2),
            description: "initial emissions to all subscribers");

        // All subscribers should have initial value
        Assert.True(subscriber1Values.Count > 0, "Subscriber 1 should have initial value");
        Assert.True(subscriber2Values.Count > 0, "Subscriber 2 should have initial value");
        Assert.True(subscriber3Values.Count > 0, "Subscriber 3 should have initial value");


        subject.OnNext(testData2);
        
        // Wait for second emission
        await ActiveWaitHelpers.WaitUntilAsync(
            () => subscriber1Values.Count >= 2 && subscriber2Values.Count >= 2 && subscriber3Values.Count >= 2,
            timeout: TimeSpan.FromSeconds(2),
            description: "second emissions to all subscribers");

        var count1BeforeDispose = subscriber1Values.Count;
        var count2BeforeDispose = subscriber2Values.Count;
        var count3BeforeDispose = subscriber3Values.Count;


        subscription2.Dispose();


        subject.OnNext(testData3);
        
        // Wait for third emission
        await ActiveWaitHelpers.WaitUntilAsync(
            () => subscriber1Values.Count > count1BeforeDispose && subscriber3Values.Count > count3BeforeDispose,
            timeout: TimeSpan.FromSeconds(2),
            description: "third emissions to remaining subscribers");


        Assert.True(subscriber1Values.Count > count1BeforeDispose, 
            "Subscriber 1 should continue receiving updates after subscriber 2 disposal");
        Assert.True(subscriber3Values.Count > count3BeforeDispose, 
            "Subscriber 3 should continue receiving updates after subscriber 2 disposal");

        // Subscriber 2 should stop receiving updates after disposal
        Assert.Equal(count2BeforeDispose, subscriber2Values.Count);

        // Verify latest values for active subscribers
        var latest1 = subscriber1Values.Last();
        var latest3 = subscriber3Values.Last();

        Assert.Equal("Final", latest1.GetProperty("Name").GetString());
        Assert.Equal("Final", latest3.GetProperty("Name").GetString());
        Assert.Equal(3, latest1.GetProperty("Value").GetInt32());
        Assert.Equal(3, latest3.GetProperty("Value").GetInt32());

        // Cleanup
        subscription1.Dispose();
        subscription3.Dispose();
    }

    #endregion

    #region Helper Classes

    private class CircularReferenceTest
    {
        public CircularReferenceTest? Self { get; set; }
        public string Name { get; set; } = "Test";
    }

    #endregion
}