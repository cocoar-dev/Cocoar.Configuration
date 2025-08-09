using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Cocoar.Configuration;
using Cocoar.Configuration.Providers.Abstractions;
using Xunit;

public class ConfigManagerOrchestrationTests
{
    private readonly struct Unit { public static readonly Unit Default = new(); }

    private sealed class CountingProvider(CountingProvider.Options options)
        : ConfigSourceProvider<CountingProvider.Options, CountingProvider.Query>(options)
    {
        public sealed class Options(string key) : ISourceProviderInstanceOptions { public string Key => key; }
        public sealed class Query(string id, IObservable<Unit> trigger) : ISourceProviderQueryOptions
        {
            public string Id => id;
            public IObservable<Unit> Trigger => trigger;
            public string? WrapperPath => null;
        }

        public static int CallCount;

        public override Task<System.Text.Json.JsonElement> GetValueAsync(Query query, System.Threading.CancellationToken ct = default)
        {
            System.Threading.Interlocked.Increment(ref CallCount);
            using var doc = System.Text.Json.JsonDocument.Parse("{\"ok\":true}");
            return Task.FromResult(doc.RootElement.Clone());
        }

        public override IObservable<System.Text.Json.JsonElement> Changes(Query query)
        {
            return query.Trigger.Select(_ =>
            {
                using var doc = System.Text.Json.JsonDocument.Parse("{\"ok\":true}");
                return doc.RootElement.Clone();
            });
        }
    }

    [Fact]
    public async Task Initialize_Does_Not_Recompute_From_Subscription_And_Recomputes_On_Change()
    {
        // Arrange
        CountingProvider.CallCount = 0;
        var changeBus = new Subject<Unit>();
        var rule = ConfigRule.Create<CountingProvider, CountingProvider.Options, CountingProvider.Query>(
            new CountingProvider.Options("K"),
            new CountingProvider.Query("Q", changeBus.AsObservable()),
            new ConfigTypeDefinition(typeof(object)),
            useWhen: () => true,
            required: true);

        // Act: Initialize triggers exactly one compute
        _ = new ConfigManager(new[] { rule }).Initialize();

        // Assert: exactly one call so far
        Assert.Equal(1, CountingProvider.CallCount);

        // Emit a change and wait briefly
        changeBus.OnNext(Unit.Default);
        await Task.Delay(50);

        // Assert: recomputed once more
        Assert.Equal(2, CountingProvider.CallCount);
    }
}
