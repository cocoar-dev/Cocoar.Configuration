using System.Text.Json;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Tests;

/*
 * SnapshotChangeDeletionTests
 * ---------------------------
 * PURPOSE
 *   Verifies that when a provider omits keys it previously contributed, those keys are removed
 *   from the merged configuration (unless superseded by later rules) and are not resurrected by
 *   subsequent unrelated updates.
 *
 * WHY THIS IS NEEDED
 *   After switching to per-rule flattened contributions (instead of storing full merged snapshots),
 *   deletions require explicit handling so stale keys do not persist indefinitely. This test guards
 *   against regressions in the deletion removal path within the orchestrator.
 *
 * WHAT IT DOES
 *   - Initial state has keys A,B.
 *   - Update removes B and adds C; asserts B disappears and C appears.
 *   - A later update modifies A only; ensures B does NOT reappear and C persists.
 */
public class SnapshotChangeDeletionTests
{
    private sealed class MutableProvider : ConfigurationProvider
    {
        private readonly Subject<JsonElement> _changes = new();
        public Dictionary<string, JsonElement> State = new();
        public int FetchCount;

        public MutableProvider()
        {
        }

        public MutableProvider(IProviderConfiguration _)
        {
        }

        public override IObservable<JsonElement> Changes(IProviderQuery query) => _changes;

        public override Task<JsonElement> FetchConfigurationAsync(IProviderQuery query,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref FetchCount);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(State));
            return Task.FromResult(doc.RootElement.Clone());
        }

        public void Apply(Action<Dictionary<string, JsonElement>> mutator)
        {
            mutator(State);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(State));
            _changes.OnNext(doc.RootElement.Clone());
        }
    }

    private sealed class Opts : IProviderConfiguration
    {
        private readonly string _k;
        public Opts(string k) => _k = k;
        public string GenerateProviderKey() => _k;
    }

    private sealed class Q : IProviderQuery
    {
        public string GenerateProviderKey() => "q";
    }

    private sealed record ConfigA(string? A, string? B, string? C);

    [Fact]
    public async Task KeyRemovalPropagatesAndDoesNotResurrect()
    {
        var p = new MutableProvider(new Opts("m1"));
        p.State["A"] = JsonDocument.Parse("\"one\"").RootElement.Clone();
        p.State["B"] = JsonDocument.Parse("\"two\"").RootElement.Clone();
        var rules = new[]
        {
            new ConfigRule(typeof(MutableProvider), new Opts("m1"), new Q(), typeof(ConfigA))
        };
        var manager = new ConfigManager(rules, null, NullLogger.Instance, (t, o) => p, debounceMilliseconds: 10).Initialize();
        await Task.Delay(30);
        var initial = manager.GetRequiredConfig<ConfigA>();
        Assert.Equal("one", initial.A);
        Assert.Equal("two", initial.B);
        Assert.Null(initial.C);

        // Remove B and add C
        p.Apply(d =>
        {
            d.Remove("B");
            d["C"] = JsonDocument.Parse("\"three\"").RootElement.Clone();
        });
        await Task.Delay(80);
        var updated = manager.GetRequiredConfig<ConfigA>();
        Assert.Equal("one", updated.A);
        Assert.Null(updated.B); // removed should not linger
        Assert.Equal("three", updated.C);

        // Ensure another unrelated change doesn't resurrect B
        p.Apply(d => { d["A"] = JsonDocument.Parse("\"one1\"").RootElement.Clone(); });
        await Task.Delay(80);
        var updated2 = manager.GetRequiredConfig<ConfigA>();
        Assert.Equal("one1", updated2.A);
        Assert.Null(updated2.B);
        Assert.Equal("three", updated2.C);
    }
}
