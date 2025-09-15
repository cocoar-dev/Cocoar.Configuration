using System.Text.Json;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reactive.Subjects;
using Cocoar.Configuration.Providers.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.Configuration.Tests;

/*
 * PartialRecomputeTests
 * ---------------------
 * PURPOSE
 *   Validates the core invariant of the incremental recompute pipeline: when a change is detected
 *   in a later rule, only the suffix (affected rule + any rules after it) is recomputed/fetched,
 *   while the prefix (earlier unchanged rules) is reconstructed purely from their stored flattened
 *   contributions WITHOUT triggering provider fetches.
 *
 * WHAT THIS SPECIFIC TEST COVERS
 *   1. Change isolated to the last rule (index 2) does NOT refetch rules at indices 0 or 1.
 *   2. A subsequent change in the middle rule (index 1) refetches that rule AND the later rule (index 2),
 *      but still does NOT refetch the earliest rule (index 0).
 *
 * WHY THIS MATTERS
 *   - Guards against regressions where prefix reuse is accidentally disabled and every change
 *     causes full-pipeline refetches (performance + provider load regression).
 *   - Ensures correctness of the earliest-index accumulation logic and per‑rule flattened contribution replay.
 *   - Protects the contract relied upon by higher-level features (e.g. future fine-grained suffix skipping).
 *
 * TEST STYLE NOTES
 *   - Baseline fetch counts are captured after initialization because multiple eager fetches can occur during
 *     warm-up depending on provider lifecycle; assertions are expressed as deltas relative to those baselines.
 *   - Distinct provider option keys ensure separate provider instances (no pooling) so fetch counts map 1:1 to rules.
 */
public class PartialRecomputeTests
{
    private sealed class CountingProvider : ConfigurationProvider
    {
        private readonly Subject<JsonElement> _changes = new();
        public int FetchCount;
        public string Name { get; }
        public int CurrentValue;
        public CountingProvider(IProviderConfiguration _) { Name = Guid.NewGuid().ToString(); }
        public CountingProvider() { Name = Guid.NewGuid().ToString(); }
        public override IObservable<JsonElement> Changes(IProviderQuery query) => _changes; // manual
        public override Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken ct = default)
        {
            var count = Interlocked.Increment(ref FetchCount);
            using var doc = JsonDocument.Parse($"{{ \"v\": {CurrentValue}, \"fetch\": {count} }}");
            return Task.FromResult(doc.RootElement.Clone());
        }
        public void Bump(int by = 1)
        {
            CurrentValue += by;
            using var doc = JsonDocument.Parse($"{{ \"v\": {CurrentValue} }}");
            _changes.OnNext(doc.RootElement.Clone());
        }
    }

    private sealed class TestProviderOptions : IProviderConfiguration
    {
        private readonly string _key;
        public TestProviderOptions(string key) { _key = key; }
        public string GenerateProviderKey() => _key;
    }
    private sealed class DummyQueryOptions : IProviderQuery { public string GenerateProviderKey() => "q"; }
    private sealed record TestConfig(int V, int Fetch);

    [Fact]
    public async Task ChangeInLaterRuleDoesNotRefetchEarlierRule()
    {
    var queryOptions = new DummyQueryOptions();
    // Distinct provider option keys to avoid pooling same instance
    var providerOptions1 = new TestProviderOptions("counting-1");
    var providerOptions2 = new TestProviderOptions("counting-2");
    var providerOptions3 = new TestProviderOptions("counting-3");
    var p1 = new CountingProvider(providerOptions1) { CurrentValue = 1 };
    var p2 = new CountingProvider(providerOptions2) { CurrentValue = 10 };
    var p3 = new CountingProvider(providerOptions3) { CurrentValue = 100 };
        var providers = new Queue<CountingProvider>(new[] { p1, p2, p3 });
        ConfigurationProvider Factory(Type t, IProviderConfiguration _) => providers.Dequeue();

    var r1 = new ConfigRule(typeof(CountingProvider), providerOptions1, queryOptions, new ConfigRegistration(typeof(TestConfig)));
    var r2 = new ConfigRule(typeof(CountingProvider), providerOptions2, queryOptions, new ConfigRegistration(typeof(TestConfig)));
    var r3 = new ConfigRule(typeof(CountingProvider), providerOptions3, queryOptions, new ConfigRegistration(typeof(TestConfig)));
        var manager = new ConfigManager(new[] { r1, r2, r3 }, NullLogger.Instance, Factory, debounceMilliseconds: 50).Initialize();

        // Warm-up fetch: each provider fetched once
    var baseP1 = p1.FetchCount;
    var baseP2 = p2.FetchCount;
    var baseP3 = p3.FetchCount;

    // Phase 1: Trigger change only on p3 (rule index 2)
        p3.Bump();
        await Task.Delay(120); // allow debounce + recompute
    Assert.Equal(baseP1, p1.FetchCount); // prefix not refetched
    Assert.Equal(baseP2, p2.FetchCount); // unaffected rule before startIndex
    Assert.Equal(baseP3 + 1, p3.FetchCount); // changed rule fetched again

    // Phase 2: Trigger change on p2; earliest index = 1 => suffix (1,2) refetched, prefix (0) reused
        p2.Bump();
        await Task.Delay(120);
    Assert.Equal(baseP1, p1.FetchCount); // earliest unchanged
    Assert.Equal(baseP2 + 1, p2.FetchCount); // refetched due to own change
    Assert.Equal(baseP3 + 2, p3.FetchCount); // suffix recompute refetched
    }
}
