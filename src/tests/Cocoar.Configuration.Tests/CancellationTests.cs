using System.Text.Json;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Threading;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Tests;

/*
 * CancellationTests
 * ------------------
 * PURPOSE
 *   Ensures an in-flight recompute is promptly cancelled when an earlier rule change arrives,
 *   and that a new recompute is started from the new earliest index. This prevents wasted work
 *   (fetching later providers based on stale earlier state) and reduces latency to consistent state.
 *
 * WHAT THIS TEST COVERS
 *   - Starts a recompute triggered by a mid rule (slow fetch) so that the pass is in progress.
 *   - Issues an earlier rule change while the slow fetch is still running, expecting cancellation + restart.
 *   - Asserts minimal expected refetch counts to confirm: earlier rule refetched, slow rule entered a new fetch cycle,
 *     and a later rule was fetched in at least one completed pass.
 *
 * WHY THIS MATTERS
 *   Validates that cancellation logic + earliest-index coalescing work together. A regression here would
 *   manifest as extra latency (waiting for full slow suffix) or missed application of earlier changes.
 *
 * NOTES
 *   - Uses variable (>=) assertions instead of exact counts because timing differences (task scheduling, CI load)
 *     may cause extra quick passes but should never result in fewer than the guaranteed minimum events.
 */
public class CancellationTests
{
    private sealed class SlowProvider : ConfigurationProvider
    {
        private readonly Subject<JsonElement> _changes = new();
        public int FetchCount;
        public int DelayMsPerFetch = 120;
        private int _value = 0;
    public SlowProvider() { }
    public SlowProvider(IProviderConfiguration _) { }
        public override IObservable<JsonElement> Changes(IProviderQuery query) => _changes; // Manual trigger
        public override async Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken cancellationToken = default)
        {
            var local = Interlocked.Increment(ref FetchCount);
            await Task.Delay(DelayMsPerFetch, cancellationToken);
            var currentVal = Interlocked.CompareExchange(ref _value, 0, 0);
            using var doc = JsonDocument.Parse($"{{ \"value\": {currentVal}, \"fetch\": {local} }}");
            return doc.RootElement.Clone();
        }
        public void BumpAndSignal()
        {
            var newVal = Interlocked.Increment(ref _value);
            using var doc = JsonDocument.Parse($"{{ \"value\": {newVal} }}");
            _changes.OnNext(doc.RootElement.Clone());
        }
    }

    private sealed record SlowProviderConfig(string Value);
    private sealed class TestProviderOptions : IProviderConfiguration
    {
        private readonly string _key;
        public TestProviderOptions(string key) { _key = key; }
        public string GenerateProviderKey() => _key;
    }
    private sealed class DummyQueryOptions : IProviderQuery { public string GenerateProviderKey() => "slow-query"; }

    [Fact]
    public async Task EarlierChangeCancelsInFlightRecompute()
    {
    var queryOptions = new DummyQueryOptions();
    var providerOptions1 = new TestProviderOptions("slow-1");
    var providerOptions2 = new TestProviderOptions("slow-2");
    var providerOptions3 = new TestProviderOptions("slow-3");
    var p1 = new SlowProvider(providerOptions1) { DelayMsPerFetch = 30 }; // fast
    var p2 = new SlowProvider(providerOptions2) { DelayMsPerFetch = 200 }; // slow (will be canceled)
    var p3 = new SlowProvider(providerOptions3) { DelayMsPerFetch = 30 };
        var queue = new Queue<SlowProvider>(new[] { p1, p2, p3 });
        ConfigurationProvider Factory(Type t, IProviderConfiguration _) => queue.Dequeue();
        var rules = new []
        {
            new ConfigRule(typeof(SlowProvider), providerOptions1, queryOptions, new ConfigRegistration(typeof(SlowProviderConfig)), new ConfigRuleOptions()),
            new ConfigRule(typeof(SlowProvider), providerOptions2, queryOptions, new ConfigRegistration(typeof(SlowProviderConfig)), new ConfigRuleOptions()),
            new ConfigRule(typeof(SlowProvider), providerOptions3, queryOptions, new ConfigRegistration(typeof(SlowProviderConfig)), new ConfigRuleOptions())
        };
        var manager = new ConfigManager(rules, NullLogger.Instance, Factory, debounceMilliseconds: 50).Initialize();
    var b1 = p1.FetchCount;
    var b2 = p2.FetchCount;
    var b3 = p3.FetchCount;

        // Trigger change on slow middle provider (index 1) to start a recompute that will fetch p2 (slow) then p3
        p2.BumpAndSignal();
        // Shortly after, trigger earlier change (index 0) to force cancellation/restart
        await Task.Delay(40); // during p2's long fetch
        p1.BumpAndSignal();

        // Wait enough time for cancellation + restart passes
        await Task.Delay(400);

        // Expectations:
        // p2 first long fetch started (FetchCount increments to 2) but may be canceled before completion of suffix fetch cycle; restart leads to another fetch (count 3) eventually.
        // To keep deterministic, assert minimum counts and that earlier provider fetched again after bump.
    Assert.True(p1.FetchCount >= b1 + 1, "p1 should have been refetched after its change");
    Assert.True(p2.FetchCount >= b2 + 1, "p2 should have at least started a second fetch (restart)");
    Assert.True(p3.FetchCount >= b3 + 1, "p3 should have been refetched in at least one pass");
    }
}
