using System.Text.Json;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Providers;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

/// <summary>
/// Differential correctness fuzz tests that prove functional correctness of the incremental recompute engine.
/// 
/// PURPOSE:
///   Prove functional correctness of the incremental recompute engine irrespective of cancellation,
///   debounce or partial-prefix reuse by comparing the engine's published end-state to a canonical
///   naive full recomputation after randomized change sequences.
/// 
/// APPROACH:
///   - Build N provider-backed rules with deterministic injection
///   - Apply waves of random mutations (add/update/delete) to randomly chosen providers
///   - After waves settle, capture the published aggregate JSON
///   - Independently perform a full recompute by re-running selection + flatten/merge logic
///   - Assert exact key/value parity (last-write-wins semantics preserved)
/// 
/// VALIDATION:
///   - Published config exists (atomic commit succeeded)
///   - Flattened map size and every key/value matches naive merge exactly
///   - No lost updates due to cancellation, incorrect deletion propagation, or ordering errors
/// </summary>
[Trait("Type", "Fuzz")]
[Trait("Provider", "ConfigManager")]
[Trait("Feature", "CorrectnessValidation")]
public class DifferentialCorrectnessFuzzTests : IDisposable
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

    public record FuzzConfig(Dictionary<string, object> Data);

    /// <summary>
    /// Core fuzz test that validates correctness under random change sequences.
    /// This is the most important test for ensuring incremental recompute correctness.
    /// </summary>
    [Fact]
    public async Task RandomChangeSequences_ProduceCorrectIncrementalResults()
    {
        const int providerCount = 6;
        const int waveCount = 5;
        const int changesPerWave = 8;


        var providers = new List<BehaviorSubject<FuzzConfig>>();
        var rules = new List<ConfigRule>();

        for (var i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                [$"provider{i}_id"] = i,
                [$"provider{i}_value"] = 0,
                ["shared_key"] = $"from_provider_{i}" // Last writer wins
            };

            var subject = new BehaviorSubject<FuzzConfig>(new(initialData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<FuzzConfig>, ObservableProviderOptions<FuzzConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(FuzzConfig),
                new());

            rules.Add(rule);
        }

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50);
        TrackForDisposal(configManager);
        configManager.Initialize();

        var random = new Random(42); // Deterministic seed for reproducible tests


        for (var wave = 0; wave < waveCount; wave++)
        {
            for (var change = 0; change < changesPerWave; change++)
            {
                var providerIndex = random.Next(providerCount);
                var operation = random.Next(3); // 0=update, 1=add, 2=delete

                var currentProvider = providers[providerIndex];
                var currentData = new Dictionary<string, object>(currentProvider.Value.Data);

                switch (operation)
                {
                    case 0: // Update existing key
                        var existingKey = currentData.Keys.FirstOrDefault();
                        if (existingKey != null)
                        {
                            currentData[existingKey] = $"updated_wave{wave}_change{change}";
                        }
                        break;

                    case 1: // Add new key
                        var newKey = $"dynamic_p{providerIndex}_w{wave}_c{change}";
                        currentData[newKey] = random.Next(1000);
                        break;

                    case 2: // Delete key (if exists)
                        var keyToDelete = currentData.Keys.Where(k => k.StartsWith("dynamic_")).FirstOrDefault();
                        if (keyToDelete != null)
                        {
                            currentData.Remove(keyToDelete);
                        }
                        break;
                }

                // Always update shared_key to test last-writer-wins
                currentData["shared_key"] = $"from_provider_{providerIndex}_wave{wave}";

                currentProvider.OnNext(new(currentData));
                
                // Small random delay to create timing variations
                await Task.Delay(random.Next(1, 10));
            }

            // Wait between waves for processing
            await Task.Delay(100);
        }

        // Wait for final settling
        await Task.Delay(300);


        var incrementalConfig = configManager.GetConfig<FuzzConfig>();
        var naiveResult = ComputeNaiveFullMerge(providers.Select(p => p.Value).ToList());

        ValidateResultsMatch(incrementalConfig, naiveResult);
    }

    /// <summary>
    /// Tests correctness under high-frequency change bursts.
    /// Validates that debouncing and coalescing don't lose changes.
    /// </summary>
    [Fact]
    public async Task HighFrequencyChangeBursts_MaintainCorrectness()
    {
        const int providerCount = 4;
        

        var providers = new List<BehaviorSubject<FuzzConfig>>();
        var rules = new List<ConfigRule>();

        for (var i = 0; i < providerCount; i++)
        {
            var initialData = new Dictionary<string, object>
            {
                [$"burst_counter"] = 0,
                [$"provider_id"] = i
            };

            var subject = new BehaviorSubject<FuzzConfig>(new(initialData));
            providers.Add(subject);
            TrackForDisposal(subject);

            var rule = ConfigRule.Create<ObservableProvider<FuzzConfig>, ObservableProviderOptions<FuzzConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(FuzzConfig),
                new());

            rules.Add(rule);
        }

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 100);
        TrackForDisposal(configManager);
        configManager.Initialize();


        for (var burst = 0; burst < 3; burst++)
        {
            // Rapid fire changes within debounce window
            for (var rapid = 0; rapid < 20; rapid++)
            {
                var providerIndex = rapid % providerCount;
                var data = new Dictionary<string, object>
                {
                    ["burst_counter"] = burst * 100 + rapid,
                    ["provider_id"] = providerIndex,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                providers[providerIndex].OnNext(new(data));
                await Task.Delay(1); // Very rapid changes
            }

            // Wait for burst to settle
            await Task.Delay(200);
        }

        // Final wait for complete processing
        await Task.Delay(300);


        var incrementalConfig = configManager.GetConfig<FuzzConfig>();
        var naiveResult = ComputeNaiveFullMerge(providers.Select(p => p.Value).ToList());

        ValidateResultsMatch(incrementalConfig, naiveResult);
        
        // Additional validation: Ensure final values reflect the last burst
        if (incrementalConfig != null)
        {
            var burstCounter = ((JsonElement)incrementalConfig.Data["burst_counter"]).GetInt32();
            Assert.True(burstCounter >= 200, $"Config should reflect final burst values, got {burstCounter}");
        }
    }

    /// <summary>
    /// Performs a naive full recompute for correctness comparison.
    /// This implements the same merge logic but without incremental optimizations.
    /// </summary>
    private Dictionary<string, object> ComputeNaiveFullMerge(List<FuzzConfig> providerStates)
    {
        var result = new Dictionary<string, object>();

        // Apply each provider's configuration in order (last writer wins)
        foreach (var config in providerStates)
        {
            foreach (var kvp in config.Data)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Validates that incremental and naive results match exactly.
    /// </summary>
    private void ValidateResultsMatch(FuzzConfig? incrementalConfig, Dictionary<string, object> naiveResult)
    {
        Assert.NotNull(incrementalConfig);
        
        // Convert incremental result to flat dictionary
        var incrementalFlat = new Dictionary<string, object>(incrementalConfig.Data);


        Assert.Equal(naiveResult.Count, incrementalFlat.Count);

        foreach (var kvp in naiveResult)
        {
            Assert.True(incrementalFlat.ContainsKey(kvp.Key), 
                $"Incremental result missing key: {kvp.Key}");

            var incrementalValue = incrementalFlat[kvp.Key];
            var naiveValue = kvp.Value;

            // Handle JsonElement comparison
            if (incrementalValue is JsonElement jsonElement)
            {
                var incrementalJson = jsonElement.GetRawText();
                var naiveJson = JsonSerializer.Serialize(naiveValue);
                if (!string.Equals(naiveJson, incrementalJson, StringComparison.Ordinal))
                {
                    Assert.Fail($"Value mismatch for key {kvp.Key}: naive={naiveJson}, incremental={incrementalJson}");
                }
            }
            else
            {
                if (!Equals(naiveValue, incrementalValue))
                {
                    Assert.Fail($"Value mismatch for key {kvp.Key}: naive={naiveValue}, incremental={incrementalValue}");
                }
            }
        }
    }
}
