using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Core;

public class SelectionHashGatingTests
{
    private sealed class MutableProvider : ConfigurationProvider<MutableProvider.Options, MutableProvider.Query>
    {
        public sealed class Options : IProviderConfiguration
        {
        }

        public sealed class Query(IObservable<JsonElement> stream) : IProviderQuery
        {
            public IObservable<JsonElement> Stream => stream;
        }

        public MutableProvider(Options options) : base(options)
        {
        }

        public static int FetchCount;
        private JsonElement _current;

        public override Task<JsonElement> FetchConfigurationAsync(Query query, CancellationToken ct = default)
        {
            Interlocked.Increment(ref FetchCount);
            return Task.FromResult(_current);
        }

        public override IObservable<JsonElement> Changes(Query query)
        {
            var subject = new System.Reactive.Subjects.Subject<JsonElement>();
            query.Stream.Subscribe(e =>
            {
                _current = e;
                subject.OnNext(e);
            });
            return subject;
        }
    }

    [Fact]
    public async Task Selection_hash_gating_suppresses_unchanged_selected_subtree()
    {
        MutableProvider.FetchCount = 0;

        static JsonElement Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        // Initial value (value = 1)
        var initial = Parse("{\"select\":{\"value\":1},\"noise\":0}");
        // Same selected subtree (value still 1), noise changes
        var noiseChange = Parse("{\"select\":{\"value\":1},\"noise\":1}");
        // Selected subtree changes (value = 2)
        var selectedChange = Parse("{\"select\":{\"value\":2},\"noise\":2}");

        // BehaviorSubject ensures initial value is available to subscriber before first fetch
        var subject = new System.Reactive.Subjects.BehaviorSubject<JsonElement>(initial);

        var rule = ConfigRule
            .Create<MutableProvider, MutableProvider.Options, MutableProvider.Query>(
                new MutableProvider.Options(),
                new MutableProvider.Query(subject),
                new ConfigRegistration(typeof(object)),
                new ConfigRuleOptions(Required: true, SelectPath: "select"));

        var mgr = new ConfigManager(new[] { rule }, NullLogger.Instance).Initialize();

        Assert.Equal(1, MutableProvider.FetchCount); // initial fetch should succeed with selection

        // First change: selected subtree identical -> one recompute expected (hash was null before, now set)
        subject.OnNext(noiseChange);
        await Task.Delay(400); // allow debounce window
        Assert.Equal(2, MutableProvider.FetchCount);

        // Second change: identical again -> suppressed
        subject.OnNext(noiseChange);
        await Task.Delay(400);
        Assert.Equal(2, MutableProvider.FetchCount);

        // Third change: selected subtree different -> recompute
        subject.OnNext(selectedChange);
        await Task.Delay(400);
        Assert.Equal(3, MutableProvider.FetchCount);
    }
}
