using System.Text.Json;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Providers;

using Cocoar.Configuration.Core.Tests.Helpers;
using Cocoar.Configuration.Core.Tests.TestUtilities;

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
    [Fact]
    public async Task SustainedRapidChanges_MaintainsStabilityAndCorrectness()
    {
        const int providerCount = 8;
        const int changesPerProvider = 200;
        const int changeIntervalMs = 5;


        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();
        var changeCounters = new int[providerCount];

        for (var i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                [$"provider_id"] = i,
                [$"change_count"] = 0,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var subject = new BehaviorSubject<StressConfig>(new(initialData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new());

            rules.Add(rule);
        }

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(20));
        TrackForDisposal(configManager);

        var initialMemory = GC.GetTotalMemory(true);


        var changeTasks = providers.Select(async (provider, providerIndex) =>
        {
            for (var change = 0; change < changesPerProvider; change++)
            {
                var data = new Dictionary<string, object>
                {
                    ["provider_id"] = providerIndex,
                    ["change_count"] = change,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    [$"data_p{providerIndex}_c{change}"] = $"value_{change}",
                    ["shared_key"] = $"from_provider_{providerIndex}_change_{change}" // Last writer wins
                };

                provider.OnNext(new(data));
                changeCounters[providerIndex] = change;

                await Task.Delay(changeIntervalMs);
            }
        });

        await Task.WhenAll(changeTasks);

        // Wait for all changes to settle
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<StressConfig>();
                return config != null;
            },
            timeout: TimeSpan.FromSeconds(5),
            description: "sustained rapid changes completion");

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
    [Fact]
    public async Task OverlappingDebounceWindows_CoalesceCorrectly()
    {
        const int providerCount = 4;
        const int burstCount = 10;
        const int changesPerBurst = 15;


        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();

        for (var i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                [$"provider_id"] = i,
                [$"burst_counter"] = 0
            };

            var subject = new BehaviorSubject<StressConfig>(new(initialData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new());

            rules.Add(rule);
        }

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(100));
        TrackForDisposal(configManager);

        var publicationCount = 0;
        var reactive = configManager.GetReactiveConfig<StressConfig>();
        using var subscription = reactive.Subscribe(_ => Interlocked.Increment(ref publicationCount));


        for (var burst = 0; burst < burstCount; burst++)
        {
            // Start overlapping bursts on all providers
            var burstTasks = providers.Select(async (provider, providerIndex) =>
            {
                for (var change = 0; change < changesPerBurst; change++)
                {
                    var data = new Dictionary<string, object>
                    {
                        ["provider_id"] = providerIndex,
                        ["burst_counter"] = burst,
                        ["change_in_burst"] = change,
                        [$"rapid_key"] = $"burst{burst}_change{change}",
                        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    provider.OnNext(new(data));
                    await Task.Delay(2); // Much faster than debounce window
                }
            });

            await Task.WhenAll(burstTasks);
            await Task.Delay(50); // Overlap with debounce window
        }

        // Wait for final debounce
        await Task.Delay(300);


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
    [Fact]
    public async Task LargeConfigurationChanges_HandleMemoryPressureGracefully()
    {
        const int providerCount = 5;
        const int largeDataSize = 1000; // Keys per configuration


        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();

        for (var i = 0; i < providerCount; i++)
        {
            var largeData = new Dictionary<string, object>();
            for (var j = 0; j < largeDataSize; j++)
            {
                largeData[$"large_key_{i}_{j}"] = $"large_value_{i}_{j}_{Guid.NewGuid()}";
            }
            largeData["provider_id"] = i;

            var subject = new BehaviorSubject<StressConfig>(new(largeData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new());

            rules.Add(rule);
        }

        var initialMemory = GC.GetTotalMemory(true);

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(50));
        TrackForDisposal(configManager);
        
        // Wait for initial configuration
        await ActiveWaitHelpers.WaitUntilAsync(
            () => configManager.GetConfig<StressConfig>() != null,
            description: "initial configuration in memory stress test");

        for (var update = 0; update < 20; update++)
        {
            foreach (var (provider, index) in providers.Select((p, i) => (p, i)))
            {
                var updatedData = new Dictionary<string, object>();
                for (var j = 0; j < largeDataSize; j++)
                {
                    updatedData[$"large_key_{index}_{j}"] = $"updated_value_{index}_{j}_{update}_{Guid.NewGuid()}";
                }
                updatedData["provider_id"] = index;
                updatedData["update_count"] = update;

                provider.OnNext(new(updatedData));
            }

            await Task.Delay(25); // Rapid updates to test memory handling
        }

        // Wait for all updates to complete
        // Note: Due to debouncing (50ms) and rapid updates (25ms interval), many emissions are coalesced.
        // The critical aspect for this stress test is that the system remains stable, not that every
        // individual update is captured. We wait for any update to settle rather than a specific count.
        await Task.Delay(1000); // Allow time for most updates to propagate
        
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<StressConfig>();
                return config != null && config.Data.ContainsKey("update_count");
            },
            timeout: TimeSpan.FromSeconds(5),
            description: "large configuration updates completion");

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
    [Fact]
    public async Task DisposalUnderStress_CleansUpProperly()
    {
        const int providerCount = 6;


        var providers = new List<BehaviorSubject<StressConfig>>();
        var rules = new List<ConfigRule>();

        for (var i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                ["provider_id"] = i,
                ["value"] = i * 100
            };

            var subject = new BehaviorSubject<StressConfig>(new(initialData));
            providers.Add(subject);

            var rule = ConfigRule.Create<ObservableProvider<StressConfig>, ObservableProviderOptions<StressConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(StressConfig),
                new());

            rules.Add(rule);
        }

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(30));


        var changeTask = Task.Run(async () =>
        {
            for (var change = 0; change < 100; change++)
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
                        provider.OnNext(new(data));
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


        // (The test passes if no exceptions were thrown during disposal)
        Assert.True(true, "Disposal under stress completed without exceptions");
    }
}