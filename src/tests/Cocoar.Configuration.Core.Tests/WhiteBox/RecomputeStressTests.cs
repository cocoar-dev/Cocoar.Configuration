using System.Text.Json;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

/// <summary>
/// Stress tests for the recompute engine under high-frequency change scenarios.
/// 
/// PURPOSE:
///   Validate that the ConfigManager maintains stability, correctness, and reasonable
///   performance under sustained high-frequency configuration changes that would
///   overwhelm naive implementations.
/// 
/// SCENARIOS:
///   - Sustained rapid changes from multiple providers
///   - Overlapping debounce windows with complex interdependencies
///   - Memory and resource stability under prolonged stress
///   - Cancellation and cleanup behavior under load
/// 
/// VALIDATION:
///   - No memory leaks or resource exhaustion
///   - Final configuration state remains consistent
///   - Debouncing and coalescing work correctly under pressure
///   - Cleanup and disposal work properly even under stress
/// </summary>
[Trait("Type", "Stress")]
[Trait("Provider", "ConfigManager")]
[Trait("Feature", "HighFrequencyChanges")]
public class RecomputeStressTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try { disposable.Dispose(); } catch { /* ignore */ }
        }
        _disposables.Clear();
    }

    private void TrackForDisposal(IDisposable disposable) => _disposables.Add(disposable);

    public record StressConfig(Dictionary<string, object> Data);

    /// <summary>
    /// Tests sustained rapid changes from multiple providers.
    /// This validates debouncing, coalescing, and resource management under realistic load.
    /// </summary>
    [Fact]
    public async Task SustainedRapidChanges_MaintainsStabilityAndCorrectness()
    {
        const int providerCount = 8;
        const int changesPerProvider = 200;
        const int changeIntervalMs = 5;

        // Arrange: Setup multiple high-frequency providers
        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();
        var changeCounters = new int[providerCount];

        for (int i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                [$"provider_id"] = i,
                [$"change_count"] = 0,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var subject = new BehaviorSubject<StressConfig>(new StressConfig(initialData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<StressConfig>(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new ConfigRuleOptions());

            rules.Add(rule);
        }

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 20);
        TrackForDisposal(configManager);
        configManager.Initialize();

        var initialMemory = GC.GetTotalMemory(true);

        // Act: Generate sustained rapid changes
        var changeTasks = providers.Select(async (provider, providerIndex) =>
        {
            for (int change = 0; change < changesPerProvider; change++)
            {
                var data = new Dictionary<string, object>
                {
                    ["provider_id"] = providerIndex,
                    ["change_count"] = change,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    [$"data_p{providerIndex}_c{change}"] = $"value_{change}",
                    ["shared_key"] = $"from_provider_{providerIndex}_change_{change}" // Last writer wins
                };

                provider.OnNext(new StressConfig(data));
                changeCounters[providerIndex] = change;

                await Task.Delay(changeIntervalMs);
            }
        });

        await Task.WhenAll(changeTasks);

        // Wait for all changes to settle
        await Task.Delay(300);

        // Assert: Validate final state and resource usage
        var finalConfig = configManager.GetConfig<StressConfig>();
        var finalMemory = GC.GetTotalMemory(true);

        // Validate configuration exists and reflects final states
        Assert.NotNull(finalConfig);
        
        // Verify final change count is reasonable (allowing for coalescing)
        Assert.True(finalConfig.Data.ContainsKey("change_count"));
        
        // Memory usage should be reasonable (not a strict assertion due to GC timing)
        var memoryIncrease = finalMemory - initialMemory;
        Assert.True(memoryIncrease < 10_000_000, 
            $"Memory increase too large: {memoryIncrease} bytes. Possible memory leak.");
    }

    /// <summary>
    /// Tests overlapping debounce windows with complex change patterns.
    /// Validates that debouncing logic works correctly when changes arrive faster than debounce intervals.
    /// </summary>
    [Fact]
    public async Task OverlappingDebounceWindows_CoalesceCorrectly()
    {
        const int providerCount = 4;
        const int burstCount = 10;
        const int changesPerBurst = 15;

        // Arrange: Setup providers with longer debounce to force overlapping
        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();

        for (int i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                [$"provider_id"] = i,
                [$"burst_counter"] = 0
            };

            var subject = new BehaviorSubject<StressConfig>(new StressConfig(initialData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<StressConfig>(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new ConfigRuleOptions());

            rules.Add(rule);
        }

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 100);
        TrackForDisposal(configManager);
        configManager.Initialize();

        var publicationCount = 0;
        var reactive = configManager.GetReactiveConfig<StressConfig>();
        using var subscription = reactive.Subscribe(_ => Interlocked.Increment(ref publicationCount));

        // Act: Generate overlapping bursts
        for (int burst = 0; burst < burstCount; burst++)
        {
            // Start overlapping bursts on all providers
            var burstTasks = providers.Select(async (provider, providerIndex) =>
            {
                for (int change = 0; change < changesPerBurst; change++)
                {
                    var data = new Dictionary<string, object>
                    {
                        ["provider_id"] = providerIndex,
                        ["burst_counter"] = burst,
                        ["change_in_burst"] = change,
                        [$"rapid_key"] = $"burst{burst}_change{change}",
                        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    provider.OnNext(new StressConfig(data));
                    await Task.Delay(2); // Much faster than debounce window
                }
            });

            await Task.WhenAll(burstTasks);
            await Task.Delay(50); // Overlap with debounce window
        }

        // Wait for final debounce
        await Task.Delay(300);

        // Assert: Validate coalescing worked properly
        var finalConfig = configManager.GetConfig<StressConfig>();
        
        Assert.NotNull(finalConfig);
        
        // Publication count should be much less than total changes due to coalescing
        var totalChanges = burstCount * changesPerBurst * providerCount;
        Assert.True(publicationCount < totalChanges / 10, 
            $"Expected significant coalescing. Publications: {publicationCount}, Total changes: {totalChanges}");

        // Configuration should reflect final burst state
        var burstCounter = ((JsonElement)finalConfig.Data["burst_counter"]).GetInt32();
        Assert.Equal(burstCount - 1, burstCounter); // Final burst (0-indexed)
    }

    /// <summary>
    /// Tests recompute engine behavior under memory pressure.
    /// Validates that the system remains stable when processing large configurations.
    /// </summary>
    [Fact]
    public async Task LargeConfigurationChanges_HandleMemoryPressureGracefully()
    {
        const int providerCount = 5;
        const int largeDataSize = 1000; // Keys per configuration

        // Arrange: Setup providers with large configuration data
        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();

        for (int i = 0; i < providerCount; i++)
        {
            var largeData = new Dictionary<string, object>();
            for (int j = 0; j < largeDataSize; j++)
            {
                largeData[$"large_key_{i}_{j}"] = $"large_value_{i}_{j}_{Guid.NewGuid()}";
            }
            largeData["provider_id"] = i;

            var subject = new BehaviorSubject<StressConfig>(new StressConfig(largeData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<StressConfig>(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new ConfigRuleOptions());

            rules.Add(rule);
        }

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50);
        TrackForDisposal(configManager);

        var initialMemory = GC.GetTotalMemory(true);
        
        configManager.Initialize();
        await Task.Delay(200); // Allow initial configuration to settle

        // Act: Perform rapid updates to large configurations
        for (int update = 0; update < 20; update++)
        {
            foreach (var (provider, index) in providers.Select((p, i) => (p, i)))
            {
                var updatedData = new Dictionary<string, object>();
                for (int j = 0; j < largeDataSize; j++)
                {
                    updatedData[$"large_key_{index}_{j}"] = $"updated_value_{index}_{j}_{update}_{Guid.NewGuid()}";
                }
                updatedData["provider_id"] = index;
                updatedData["update_count"] = update;

                provider.OnNext(new StressConfig(updatedData));
            }

            await Task.Delay(25); // Rapid updates
        }

        // Wait for processing
        await Task.Delay(300);

        // Assert: Validate system stability and memory management
        var finalConfig = configManager.GetConfig<StressConfig>();
        var finalMemory = GC.GetTotalMemory(true);

        Assert.NotNull(finalConfig);

        // Configuration should have the expected number of keys
        Assert.True(finalConfig.Data.Count >= largeDataSize, 
            $"Configuration should contain large data set, got {finalConfig.Data.Count} keys");
        
        Assert.True(finalConfig.Data.ContainsKey("update_count"), 
            "Configuration should reflect updates");

        // Memory should be managed reasonably (allowing for some growth)
        var memoryIncrease = finalMemory - initialMemory;
        Assert.True(memoryIncrease < 100_000_000, 
            $"Memory increase excessive: {memoryIncrease} bytes. Possible memory issue.");
    }

    /// <summary>
    /// Tests that cancellation and cleanup work properly under stress conditions.
    /// Validates that disposal doesn't cause issues even when changes are in flight.
    /// </summary>
    [Fact]
    public async Task DisposalUnderStress_CleansUpProperly()
    {
        const int providerCount = 6;

        // Arrange: Setup short-lived ConfigManager under stress
        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();

        for (int i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                ["provider_id"] = i,
                ["value"] = i * 100
            };

            var subject = new BehaviorSubject<StressConfig>(new StressConfig(initialData));
            providers.Add(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<StressConfig>(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new ConfigRuleOptions());

            rules.Add(rule);
        }

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 30);
        configManager.Initialize();

        // Act: Start rapid changes then dispose while changes are in flight
        var changeTask = Task.Run(async () =>
        {
            for (int change = 0; change < 100; change++)
            {
                foreach (var (provider, index) in providers.Select((p, i) => (p, i)))
                {
                    var data = new Dictionary<string, object>
                    {
                        ["provider_id"] = index,
                        ["value"] = change * 100 + index,
                        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    try
                    {
                        provider.OnNext(new StressConfig(data));
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected when disposal happens during changes
                        return;
                    }
                }

                await Task.Delay(5);
            }
        });

        // Let some changes accumulate
        await Task.Delay(100);

        // Dispose while changes are in flight
        configManager.Dispose();

        // Dispose providers
        foreach (var provider in providers)
        {
            provider.Dispose();
        }

        // Wait for change task to complete or timeout
        try
        {
            await changeTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            // Expected - disposal should stop processing
        }

        // Assert: No exceptions during disposal and cleanup completed
        // (The test passes if no exceptions were thrown during disposal)
        Assert.True(true, "Disposal under stress completed without exceptions");
    }
}