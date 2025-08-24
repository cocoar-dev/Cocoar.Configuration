using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cocoar.Configuration;
using Cocoar.Configuration.Providers.Abstractions;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.MicrosoftAdapter;
using Microsoft.Extensions.Primitives;

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

public class MicrosoftProviderAdapterTests
{
    [Fact]
    public void Single_source_adapter_loads_values_from_memory_source()
    {
        // Arrange: a simple in-memory configuration source
        var memData = new Dictionary<string, string?>
        {
            ["My:Section:Enabled"] = "true",
            ["My:Section:Value"] = "42"
        };
    var memSource = new SimpleInMemoryConfigurationSource(memData);

        var rule = ConfigRule.Create<
            MicrosoftConfigurationSourceProvider,
            MicrosoftConfigurationSourceProviderOptions,
            MicrosoftConfigurationSourceProviderQueryOptions>(
            new MicrosoftConfigurationSourceProviderOptions(memSource),
            new MicrosoftConfigurationSourceProviderQueryOptions(keyPrefix: "My:Section"),
            new ConfigTypeDefinition(typeof(MicrosoftProviderAdapterTests.DemoConfig))
        );

        var mgr = new ConfigManager(new[] { rule }, NullLogger.Instance).Initialize();
        var cfg = mgr.GetConfig<DemoConfig>();

        Assert.NotNull(cfg);
        Assert.True(cfg!.Enabled);
        Assert.Equal(42, cfg.Value);
    }

    public sealed class DemoConfig
    {
        public bool Enabled { get; set; }
        public int Value { get; set; }
    }

    private sealed class SimpleInMemoryConfigurationSource(IDictionary<string, string?> data) : IConfigurationSource
    {
        private readonly IDictionary<string, string?> _data = data;
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new SimpleInMemoryConfigurationProvider(_data);
    }

    private sealed class SimpleInMemoryConfigurationProvider : IConfigurationProvider
    {
        private readonly IDictionary<string, string?> _data;
        public SimpleInMemoryConfigurationProvider(IDictionary<string, string?> data)
        {
            _data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            var prefix = parentPath == null ? string.Empty : parentPath + ":";
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _data.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = key.Substring(prefix.Length);
                var idx = suffix.IndexOf(':');
                var child = idx >= 0 ? suffix.Substring(0, idx) : suffix;
                keys.Add(child);
            }
            return keys.Concat(earlierKeys).OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        }

        public IChangeToken GetReloadToken() => new Microsoft.Extensions.Primitives.CancellationChangeToken(new System.Threading.CancellationToken(false));

        public void Load() { /* no-op: data provided upfront */ }

        public void Set(string key, string? value)
        {
            _data[key] = value;
        }

        public bool TryGet(string key, out string? value) => _data.TryGetValue(key, out value);
    }
}
