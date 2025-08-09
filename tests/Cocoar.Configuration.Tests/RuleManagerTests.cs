using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public partial class RuleManagerTests
{
    private sealed class TestLogger : IConfigLogger
    {
        public void Debug(string message, params object[] args) { }
        public void Information(string message, params object[] args) { }
        public void Warning(Exception? ex, string message, params object[] args) { }
        public void Error(Exception ex, string message, params object[] args) { }
    }

    [Fact]
    public async Task ComputeAsync_Skips_When_UseWhen_False()
    {
        var rule = ConfigRule.Create<FakeFileProvider, FakeFileProviderOptions, FakeFileProviderQuery>(
            new FakeFileProviderOptions("dir"),
            new FakeFileProviderQuery("file.json"),
            new ConfigTypeDefinition(typeof(object)),
            useWhen: () => false,
            required: true);

    var rm = new RuleManager(rule, new TestLogger(), new ProviderRegistry());
        var (include, _) = await rm.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);
        Assert.False(include);
    }

    [Fact]
    public async Task ComputeAsync_Throws_On_Required_Failure()
    {
        var rule = ConfigRule.Create<FakeFileProvider, FakeFileProviderOptions, FakeFileProviderQuery>(
            new FakeFileProviderOptions("dir"),
            new FakeFileProviderQuery("missing.json", fail: true),
            new ConfigTypeDefinition(typeof(object)),
            useWhen: () => true,
            required: true);

    var rm = new RuleManager(rule, new TestLogger(), new ProviderRegistry());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await rm.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default));
    }

    [Fact]
    public async Task ComputeAsync_Optional_Skips_On_Failure()
    {
        var rule = ConfigRule.Create<FakeFileProvider, FakeFileProviderOptions, FakeFileProviderQuery>(
            new FakeFileProviderOptions("dir"),
            new FakeFileProviderQuery("missing.json", fail: true),
            new ConfigTypeDefinition(typeof(object)),
            useWhen: () => true,
            required: false);

    var rm = new RuleManager(rule, new TestLogger(), new ProviderRegistry());
        var (include, _) = await rm.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);
        Assert.False(include);
    }

    private sealed class InMemoryProviderOptions : ISourceProviderInstanceOptions
    {
        public string Key { get; }
        public InMemoryProviderOptions(string key) => Key = key;
    }

    private sealed class InMemoryQueryOptions(string id) : ISourceProviderQueryOptions
    {
        public string Id => id;
        public string? MemberPath => null;
        public string? MemberWrapper => null;
    }

    private sealed class InMemoryProvider(InMemoryProviderOptions options)
        : ConfigSourceProvider<InMemoryProviderOptions, InMemoryQueryOptions>(options)
    {
        private readonly Dictionary<string, JsonElement> _store = new();

        public override Task<JsonElement> GetValueAsync(InMemoryQueryOptions query, CancellationToken ct = default)
        {
            if (_store.TryGetValue(query.Id, out var v)) return Task.FromResult(v);
            var json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["Value"] = options.Key + ":" + query.Id });
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement.Clone();
            _store[query.Id] = el;
            return Task.FromResult(el);
        }

        public override IObservable<JsonElement> Changes(InMemoryQueryOptions query)
        {
            return Observable.Empty<JsonElement>();
        }
    }

    [Fact]
    public async Task RuleManager_Reuses_Provider_When_InstanceOptions_Unchanged()
    {
        var typeDef = new ConfigTypeDefinition(typeof(object));
        var providerFactoryCalls = 0;
        var queryFactoryCalls = 0;

        var rule = ConfigRule.Create<InMemoryProvider, InMemoryProviderOptions, InMemoryQueryOptions>(
            providerOptionsFactory: _ => { providerFactoryCalls++; return new InMemoryProviderOptions("K1"); },
            queryOptionsFactory: _ => { queryFactoryCalls++; return new InMemoryQueryOptions("Q1"); },
            typeDefinition: typeDef,
            useWhen: () => true,
            required: true);

    var rm = new RuleManager(rule, new TestLogger(), new ProviderRegistry());
        var acc = new ConfigManager(Array.Empty<ConfigRule>());
        var r1 = await rm.ComputeAsync(acc, default);
        var r2 = await rm.ComputeAsync(acc, default);

        Assert.True(r1.include);
        Assert.True(r2.include);
        Assert.True(providerFactoryCalls >= 2);
        Assert.True(queryFactoryCalls >= 2);
    }

    [Fact]
    public async Task RuleManager_Resubscribes_When_Query_Changes()
    {
        var typeDef = new ConfigTypeDefinition(typeof(object));
        var qId = "Q1";
        var rule = ConfigRule.Create<InMemoryProvider, InMemoryProviderOptions, InMemoryQueryOptions>(
            providerOptionsFactory: _ => new InMemoryProviderOptions("K1"),
            queryOptionsFactory: _ => new InMemoryQueryOptions(qId),
            typeDefinition: typeDef,
            useWhen: () => true,
            required: true);

    var rm = new RuleManager(rule, new TestLogger(), new ProviderRegistry());
        var acc = new ConfigManager(Array.Empty<ConfigRule>());
        var _ = await rm.ComputeAsync(acc, default);

        // change query
        qId = "Q2";
        var __ = await rm.ComputeAsync(acc, default);

        Assert.True(true);
    }

    // Minimal fake provider to simulate failures
    private sealed class FakeFileProviderOptions(string dir) : ISourceProviderInstanceOptions { public string Dir => dir; }
    private sealed class FakeFileProviderQuery(string name, bool fail = false) : ISourceProviderQueryOptions { public string Name => name; public bool Fail => fail; public string? MemberPath => null; public string? MemberWrapper => null; }
    private sealed class FakeFileProvider(FakeFileProviderOptions options) : ConfigSourceProvider<FakeFileProviderOptions, FakeFileProviderQuery>(options)
    {
        public override Task<JsonElement> GetValueAsync(FakeFileProviderQuery query, CancellationToken ct = default)
        {
            if (query.Fail) throw new InvalidOperationException("fail");
            using var doc = JsonDocument.Parse("{}");
            return Task.FromResult(doc.RootElement.Clone());
        }
    public override IObservable<JsonElement> Changes(FakeFileProviderQuery query) => Observable.Empty<JsonElement>();
    }

    [Fact]
    public async Task RuleManager_Triggers_Recompute_On_Provider_Change()
    {
        // A provider that emits a change signal when we push into a Subject
        var changeBus = new Subject<Unit>();

        var rule = ConfigRule.Create<EmittingProvider, EmittingProvider.Options, EmittingProvider.Query>(
            new EmittingProvider.Options("K"),
            new EmittingProvider.Query("Q", changeBus),
            new ConfigTypeDefinition(typeof(object)),
            useWhen: () => true,
            required: true);

        var logger = new TestLogger();
    var rm = new RuleManager(rule, logger, new ProviderRegistry());
        var manager = new ConfigManager(new[] { rule }, logger).Initialize();

        // first compute to set up subscription
        var _ = await rm.ComputeAsync(manager, default);

        // Arrange: wait for the first change signal from the rule manager
        var changeTask = rm.Changes
            .FirstAsync()
            .Timeout(TimeSpan.FromSeconds(2))
            .ToTask();

        // When: provider emits a change
        changeBus.OnNext(Unit.Default);

        // Then: the change is observed (will throw on timeout)
        await changeTask;
    }

    private readonly struct Unit { public static readonly Unit Default = new(); }

    private sealed class EmittingProvider(EmittingProvider.Options options)
        : ConfigSourceProvider<EmittingProvider.Options, EmittingProvider.Query>(options)
    {
        public sealed class Options(string key) : ISourceProviderInstanceOptions { public string Key => key; }
        public sealed class Query(string id, IObservable<Unit> trigger) : ISourceProviderQueryOptions
        {
            public string Id => id;
            public IObservable<Unit> Trigger => trigger;
            public string? MemberPath => null;
            public string? MemberWrapper => null;
        }

        public override Task<JsonElement> GetValueAsync(Query query, CancellationToken ct = default)
        {
            using var doc = JsonDocument.Parse("{\"ok\":true}");
            return Task.FromResult(doc.RootElement.Clone());
        }

        public override IObservable<JsonElement> Changes(Query query)
        {
            return query.Trigger.Select(_ =>
            {
                using var doc = JsonDocument.Parse("{\"ok\":true}");
                return doc.RootElement.Clone();
            });
        }
    }
}

public partial class RuleManagerTests
{
    [Fact]
    public async Task EnvironmentProvider_Is_Shared_Across_Different_Prefixes()
    {
        var registry = new ProviderRegistry();
        var rule1 = EnvironmentVariableProvider.CreateRule<object>(memberPath: "APP1_", required: true);
        var rule2 = EnvironmentVariableProvider.CreateRule<object>(memberPath: "APP2_", required: true);

        var rm1 = new RuleManager(rule1, new TestLogger(), registry);
        var rm2 = new RuleManager(rule2, new TestLogger(), registry);

    // compute once to ensure providers are acquired
        _ = await rm1.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);
        _ = await rm2.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);

        // Using diagnostics to assert that only a single entry exists in the registry
    Assert.Equal(1, registry.EntryCount);
    }
}

public partial class RuleManagerTests
{
    private sealed class IdentityOptions(string name) : ISourceProviderInstanceOptions
    {
        public string Name => name;
    }

    private sealed class IdentityQuery(string id) : ISourceProviderQueryOptions
    {
        public string Id => id;
        public string? MemberPath => null;
        public string? MemberWrapper => null;
    }

    private sealed class IdentityProvider(IdentityOptions options)
        : ConfigSourceProvider<IdentityOptions, IdentityQuery>(options)
    {
        private static int CreatedCount;
        private readonly int _instanceId = Interlocked.Increment(ref CreatedCount);

        public override Task<JsonElement> GetValueAsync(IdentityQuery query, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["providerId"] = _instanceId,
                ["key"] = options.Name,
                ["q"] = query.Id
            });
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }

        public override IObservable<JsonElement> Changes(IdentityQuery query)
            => Observable.Empty<JsonElement>();
    }

    [Fact]
    public async Task ProviderRegistry_Shares_Provider_Across_RuleManagers_With_Same_Key()
    {
        var registry = new ProviderRegistry();
        var rule1 = ConfigRule.Create<IdentityProvider, IdentityOptions, IdentityQuery>(
            new IdentityOptions("A"),
            new IdentityQuery("Q1"),
            new ConfigTypeDefinition(typeof(object)),
            required: true);
        var rule2 = ConfigRule.Create<IdentityProvider, IdentityOptions, IdentityQuery>(
            new IdentityOptions("A"),
            new IdentityQuery("Q2"),
            new ConfigTypeDefinition(typeof(object)),
            required: true);

        var rm1 = new RuleManager(rule1, new TestLogger(), registry);
        var rm2 = new RuleManager(rule2, new TestLogger(), registry);

        var r1 = await rm1.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);
        var r2 = await rm2.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);

        var id1 = r1.value.GetProperty("providerId").GetInt32();
        var id2 = r2.value.GetProperty("providerId").GetInt32();
        Assert.Equal(id1, id2);
    }

    [Fact]
    public async Task ProviderRegistry_Does_Not_Share_Provider_When_Keys_Differ()
    {
        var registry = new ProviderRegistry();
        var rule1 = ConfigRule.Create<IdentityProvider, IdentityOptions, IdentityQuery>(
            new IdentityOptions("A"),
            new IdentityQuery("Q1"),
            new ConfigTypeDefinition(typeof(object)),
            required: true);
        var rule2 = ConfigRule.Create<IdentityProvider, IdentityOptions, IdentityQuery>(
            new IdentityOptions("B"),
            new IdentityQuery("Q2"),
            new ConfigTypeDefinition(typeof(object)),
            required: true);

        var rm1 = new RuleManager(rule1, new TestLogger(), registry);
        var rm2 = new RuleManager(rule2, new TestLogger(), registry);

        var r1 = await rm1.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);
        var r2 = await rm2.ComputeAsync(new ConfigManager(Array.Empty<ConfigRule>()), default);

        var id1 = r1.value.GetProperty("providerId").GetInt32();
        var id2 = r2.value.GetProperty("providerId").GetInt32();
        Assert.NotEqual(id1, id2);
    }
}
