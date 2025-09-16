using System.Text.Json;
using System.Reactive.Subjects;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Tests;

/*
 * DifferentialCorrectnessFuzzTests
 * ---------------------------------
 * PURPOSE
 *   Prove functional correctness of the incremental recompute engine irrespective of cancellation,
 *   debounce or partial-prefix reuse by comparing the engine's published end-state to a canonical
 *   naive full recomputation after randomized change sequences.
 *
 * APPROACH
 *   - Build N provider-backed rules (mixed mount usage) with deterministic injection.
 *   - Apply waves of random mutations (add/update/delete/reset) to randomly chosen providers.
 *   - After waves settle (quiet window > debounce), capture the published aggregate JSON.
 *   - Independently perform a full recompute (no incremental optimisation) by re-running the
 *     selection + mount + flatten/merge logic in strict rule order over the providers' current payloads.
 *   - Flatten both results and assert exact key/value parity (last-write-wins semantics preserved).
 *
 * ASSERTIONS
 *   - Published config exists (atomic commit succeeded).
 *   - Flattened map size & every key/value matches naive merge exactly (strong equality, not heuristic).
 *
 * WHAT THIS CATCHES
 *   - Lost updates due to cancellation mid-pass.
 *   - Regressions where old per-rule flat contributions leak into final state.
 *   - Incorrect deletion propagation or missed key removals.
 *   - Ordering errors where later rule contributions are overwritten by earlier rule snapshots.
 *
 * LIMITATIONS
 *   - Uses a single shared registration (object) for simplicity; structure variety comes from mounts.
 *   - Does not vary rule ordering between runs (that is covered in other targeted tests if added later).
 *
 * EXTENSIONS (potential)
 *   - Parameterised runs with different debounce values.
 *   - Shuffle rule ordering between fuzz iterations.
 */

/// <summary>
/// Differential end-state equality between incremental pipeline and canonical naive recompute.
/// Ensures no optimisation alters correctness.
/// </summary>
public class DifferentialCorrectnessFuzzTests
{
    private sealed class PayloadProvider : ConfigurationProvider
    {
        private readonly Subject<JsonElement> _changes = new();
        private JsonElement _current;
        private int _version;
        private readonly string _id;
        public int FetchCount;

        public PayloadProvider(IProviderConfiguration _)
        {
            _id = Guid.NewGuid().ToString("N");
            _current = JsonDocument.Parse("{ }" ).RootElement.Clone();
        }

        public override IObservable<JsonElement> Changes(IProviderQuery query) => _changes;

        public override Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref FetchCount);
            return Task.FromResult(_current);
        }

        public void Apply(Action<Dictionary<string, object>> mutate)
        {
            var dict = new Dictionary<string, object>();
            // Start from current flattened state for additive mutations.
            // Convert current Json into nested dictionary (simple round-trip via deserialize)
            var json = _current.GetRawText();
            var nested = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            foreach (var kv in nested) dict[kv.Key] = kv.Value!;
            mutate(dict);
            dict["version"] = ++_version; // embed version to detect regressions
            var newJson = JsonSerializer.Serialize(dict);
            _current = JsonDocument.Parse(newJson).RootElement.Clone();
            // Signal raw change (selection / mount handled by rule manager)
            _changes.OnNext(_current);
        }
    }

    private sealed record ProviderCfg(string Key) : IProviderConfiguration
    {
        public string GenerateProviderKey() => Key;
    }
    private sealed record QueryCfg(string Key) : IProviderQuery
    {
        public string GenerateProviderKey() => Key;
    }

    private static JsonElement NaiveFullRecompute(List<(ConfigRule Rule, PayloadProvider Provider)> rules)
    {
        // Re-implement full merge: flatten each participating provider sequentially and last-write-wins.
        var flat = new Dictionary<string, JsonElement>();
        foreach (var (rule, provider) in rules)
        {
            // Simulate compute stages (selection + mount) like RuleManager.ComputeAsync does.
            var payload = provider.FetchConfigurationAsync(new QueryCfg("q"), CancellationToken.None).Result; // immediate (in-memory)
            var selectPath = rule.Options?.SelectPath;
            if (!string.IsNullOrWhiteSpace(selectPath))
            {
                try { payload = Json.JsonPath.SelectColonDelimited(payload, selectPath); } catch { continue; }
            }
            var mountPath = rule.Options?.MountPath;
            if (!string.IsNullOrWhiteSpace(mountPath))
            {
                payload = Json.JsonPath.WrapIfNeeded(payload, mountPath);
            }
            var contrib = JsonConfigurationProcessor.Flatten(payload);
            foreach (var kv in contrib) flat[kv.Key] = kv.Value;
        }
        return JsonConfigurationProcessor.Unflatten(flat);
    }

    public static IEnumerable<object[]> OrderSeeds()
    {
        // A few distinct seeds to permute rule order deterministically
        yield return new object[] { 111 }; 
        yield return new object[] { 222 }; 
        yield return new object[] { 333 }; 
    }

    [Theory]
    [MemberData(nameof(OrderSeeds))]
    public async Task RandomChangeSequences_EndStateMatchesNaiveFullRecompute(int orderSeed)
    {
        var rnd = new Random(2222);
        const int ruleCount = 5;
        // Mix of mounted & selected rules to exercise transformation path.
        var providers = new PayloadProvider[ruleCount];
        var rules = new List<(ConfigRule Rule, PayloadProvider Provider)>();
        var factoryQueue = new Queue<PayloadProvider>();
        for (int i = 0; i < ruleCount; i++)
        {
            var prov = new PayloadProvider(new ProviderCfg($"prov-{i}"));
            providers[i] = prov;
            var opts = new ConfigRuleOptions
            {
                Required = true,
                // Alternate selection & mount application to create nested merging variety
                SelectPath = i % 2 == 0 ? null : null, // keep simple (no selection) here; could add paths if sample JSON supports
                MountPath = i % 2 == 1 ? $"layer{i}" : null
            };
            var rule = new ConfigRule(typeof(PayloadProvider), new ProviderCfg($"prov-{i}"), new QueryCfg("q"), typeof(object), opts);
            rules.Add((rule, prov));
        }

        // Deterministic shuffle of rule order (and provider instantiation order) for this run
        var shuffled = rules.ToList();
        var orderRnd = new Random(orderSeed);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = orderRnd.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        factoryQueue = new Queue<PayloadProvider>(shuffled.Select(r => r.Provider));
        ConfigurationProvider Factory(Type _, IProviderConfiguration cfg) => factoryQueue.Dequeue();
        var cm = new ConfigManager(shuffled.Select(r => r.Rule), null, NullLogger.Instance, Factory, debounceMilliseconds: 30).Initialize();

        // Seed initial mutations
        foreach (var p in providers)
        {
            p.Apply(dict => dict[$"k{rnd.Next(1,4)}"] = rnd.Next(1000));
        }
        await Task.Delay(150); // allow initial recomputes

        // Random change waves
        for (int wave = 0; wave < 40; wave++)
        {
            int changesThisWave = rnd.Next(1, ruleCount + 1);
            for (int c = 0; c < changesThisWave; c++)
            {
                var idx = rnd.Next(0, ruleCount);
                providers[idx].Apply(d =>
                {
                    // 50% mutate existing or new key, 25% delete, 25% replace all
                    var mode = rnd.NextDouble();
                    if (mode < 0.5)
                        d[$"k{rnd.Next(1,5)}"] = rnd.Next(10000);
                    else if (mode < 0.75 && d.Count > 0)
                    {
                        var key = d.Keys.First();
                        d.Remove(key);
                    }
                    else
                    {
                        d.Clear();
                        d[$"reset{rnd.Next(1,3)}"] = rnd.Next(5000);
                    }
                });
            }
            // Small jitter inside wave
            await Task.Delay(rnd.Next(5, 25));
        }

    // Wait for quiescence (no active recompute) using polling to avoid race where a cancellation
    // triggers a final pass after the fixed delay.
    await WaitForIdleAsync(cm, providers, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5));

        // Obtain published aggregate (all rules share same registration type here)
        var published = cm.GetConfigAsJson(typeof(object));
        Assert.True(published.HasValue);
    // Use shuffled order for naive recompute to mirror live precedence
    var naive = NaiveFullRecompute(shuffled);

        // Compare flattened maps for deterministic ordering & equality
        var flatPublished = JsonConfigurationProcessor.Flatten(published.Value);
        var flatNaive = JsonConfigurationProcessor.Flatten(naive);
        Assert.Equal(flatNaive.Count, flatPublished.Count);
        foreach (var kv in flatNaive)
        {
            Assert.True(flatPublished.TryGetValue(kv.Key, out var v), $"Missing key {kv.Key}");
            Assert.Equal(kv.Value.GetRawText(), v.GetRawText());
        }
    }

    private static async Task WaitForIdleAsync(ConfigManager cm, PayloadProvider[] providers, TimeSpan quietPeriod, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int stableIterations = 0;
        var lastFetches = providers.Select(p => p.FetchCount).ToArray();
        Task? lastTask = cm.CurrentRecomputeTask; // internal visible
        while (sw.Elapsed < timeout)
        {
            await Task.Delay(75);
            var currentTask = cm.CurrentRecomputeTask;
            var fetches = providers.Select(p => p.FetchCount).ToArray();
            bool taskIdle = currentTask == null || currentTask.IsCompleted;
            bool fetchStable = true;
            for (int i = 0; i < fetches.Length; i++)
                if (fetches[i] != lastFetches[i]) { fetchStable = false; break; }
            if (taskIdle && fetchStable)
            {
                stableIterations++;
                if (stableIterations * 75 >= quietPeriod.TotalMilliseconds)
                    return; // considered idle
            }
            else
            {
                stableIterations = 0; // reset stability window
            }
            lastFetches = fetches;
            lastTask = currentTask;
        }
        // If we exit due to timeout, we allow test to proceed; any inconsistency will surface in diff assertion.
    }
}
