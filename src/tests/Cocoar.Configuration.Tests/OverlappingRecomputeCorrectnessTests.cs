using System.Text.Json;
using System.Reactive.Subjects;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Tests;

/*
 * OverlappingRecomputeCorrectnessTests
 * ------------------------------------
 * PURPOSE
 *   Stress earliest-index cancellation + restart logic with maximal overlap by issuing waves of
 *   descending-index changes (high index -> 0) so that each earlier provider invalidates in-flight
 *   recomputes initiated by later providers.
 *
 * APPROACH
 *   - Create a sequence of providers; each change embeds an incrementing version plus a provider-specific key.
 *   - Perform multiple full descending passes triggering every provider in high temporal proximity.
 *   - Allow quiescence, then examine published flattened config.
 *
 * ASSERTIONS
 *   - Each provider's key exists (its contribution not lost permanently).
 *   - At least one flattened value contains the provider's final version number, indicating latest
 *     state observed & published (guards against stale overwrite / regression).
 *
 * WHAT THIS CATCHES
 *   - Race conditions where cancellation rolls back or discards newest contribution.
 *   - Incorrect prefix reuse that re-injects stale per-rule flat contributions.
 *   - Missing final publish after rapid successive cancellations.
 *
 * LIMITATIONS
 *   - Version presence check is a pragmatic signal (we avoid embedding per-key provenance metadata in core library).
 *   - Does not assert ordering of intermediate states—only final correctness.
 *
 * EXTENSIONS (potential)
 *   - Track per-provider last published version via debug hook for stronger assertion.
 *   - Introduce mixed ascending/descending pattern.
 */

public class OverlappingRecomputeCorrectnessTests
{
    private sealed class SeqProvider : ConfigurationProvider
    {
        private readonly Subject<JsonElement> _changes = new();
        private int _version;
        private JsonElement _current;
        public int Version => _version;
        public SeqProvider(IProviderConfiguration _)
        {
            _current = JsonDocument.Parse("{ }" ).RootElement.Clone();
        }
        public override IObservable<JsonElement> Changes(IProviderQuery query) => _changes;
        public override Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(_current);
        public void Bump(string key)
        {
            var newObj = new Dictionary<string, object?>
            {
                [key] = _version,
                ["v"] = ++_version
            };
            var json = JsonSerializer.Serialize(newObj);
            _current = JsonDocument.Parse(json).RootElement.Clone();
            _changes.OnNext(_current);
        }
    }

    private sealed record PCfg(string K) : IProviderConfiguration { public string GenerateProviderKey() => K; }
    private sealed record QCfg(string K) : IProviderQuery { public string GenerateProviderKey() => K; }

    [Fact]
    public async Task DescendingIndexStorm_FinalStateReflectsLatestAllProviders()
    {
        const int ruleCount = 6;
        var providers = new SeqProvider[ruleCount];
        var rules = new List<(ConfigRule Rule, SeqProvider Provider)>();
        var queue = new Queue<SeqProvider>();
        for (int i = 0; i < ruleCount; i++)
        {
            var p = new SeqProvider(new PCfg($"p{i}"));
            providers[i] = p;
            queue.Enqueue(p);
            var rule = new ConfigRule(typeof(SeqProvider), new PCfg($"p{i}"), new QCfg("q"), new ConfigRegistration(typeof(object)));
            rules.Add((rule, p));
        }
        ConfigurationProvider Factory(Type _, IProviderConfiguration __) => queue.Dequeue();
        var cm = new ConfigManager(rules.Select(r => r.Rule), NullLogger.Instance, Factory, debounceMilliseconds: 25).Initialize();

        // Initial bump for all
        for (int i = 0; i < ruleCount; i++) providers[i].Bump($"k{i}");
        await Task.Delay(150);

        // Descending storms: always trigger earlier index after later to force cancellation restarts
        for (int wave = 0; wave < 40; wave++)
        {
            for (int idx = ruleCount - 1; idx >= 0; idx--)
            {
                providers[idx].Bump($"k{idx}");
                // minimal delay to keep overlap high
                await Task.Delay(5);
            }
        }

        // Allow final consolidation
        await Task.Delay(400);

        var published = cm.GetConfigAsJson(typeof(object));
        Assert.True(published.HasValue);

        // Flatten published and verify each provider's latest version info is reflected.
        var flat = JsonConfigurationProcessor.Flatten(published.Value);
        // Each provider sets key k{i} plus 'v'. We expect last write wins so final keys must exist.
        for (int i = 0; i < ruleCount; i++)
        {
            // Ensure its version counter value is reachable (either at k{i} or overwritten by later rule with same key). We embed provider version as value of k{i} before version increment.
            var key = $"k{i}";
            Assert.True(flat.ContainsKey(key), $"Missing key {key}");
        }
        // Ensure no regression: versions monotonically increased per provider, so published must have at least one key per provider's final version encoded somewhere.
        // (Simplified check: at least one key includes its latest version integer as raw text)
        foreach (var p in providers)
        {
            var ver = p.Version; // final version counter after increments
            bool found = flat.Any(kv => kv.Value.GetRawText().Contains(ver.ToString(), StringComparison.Ordinal));
            Assert.True(found, $"Final version {ver} for provider not reflected in published config");
        }
    }
}
