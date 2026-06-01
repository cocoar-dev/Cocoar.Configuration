using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.WritableStore;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.WritableStore;

[Trait("Type", "Unit")]
public sealed class WritableStorePatchTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly List<IDisposable> _disposables = [];

    private (ServiceProvider Provider, IWritableStore<SmtpSettings> Storage, ConfigManager Manager) Build(
        string baseJson, IStoreBackend? backend = null)
    {
        backend ??= new InMemoryBackend();
        var file = TempFileHelper.Create(baseJson);
        _disposables.Add(file);

        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => new ConfigRule[]
        {
            rules.For<SmtpSettings>().FromFile(file.FilePath).Required(),
            rules.For<SmtpSettings>().FromStore(backend),
        }));

        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);

        return (provider, provider.GetRequiredService<IWritableStore<SmtpSettings>>(), provider.GetRequiredService<ConfigManager>());
    }

    // ---------------------------------------------------------------- PatchAsync — batch

    [Fact]
    public async Task PatchAsync_SetsMultipleProperties_Atomically()
    {
        var (_, storage, manager) = Build("{\"Host\":\"old.host\",\"Port\":25,\"UseSsl\":false}");

        await storage.PatchAsync(b => b
            .Set(x => x.Host, "new.host")
            .Set(x => x.Port, 587)
            .Set(x => x.UseSsl, true));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "Port updated");

        var config = manager.GetConfig<SmtpSettings>()!;
        Assert.Equal("new.host", config.Host);
        Assert.Equal(587, config.Port);
        Assert.True(config.UseSsl);
    }

    [Fact]
    public async Task PatchAsync_SingleRecompute_ForWholeBatch()
    {
        var (provider, storage, manager) = Build("{\"Host\":\"h\",\"Port\":25,\"UseSsl\":false}");

        var reactive = provider.GetRequiredService<Cocoar.Configuration.Reactive.IReactiveConfig<SmtpSettings>>();
        var emissions = 0;
        using var sub = reactive.Subscribe(_ => Interlocked.Increment(ref emissions));
        var baseline = Volatile.Read(ref emissions);

        await storage.PatchAsync(b => b
            .Set(x => x.Host, "new.host")
            .Set(x => x.Port, 587)
            .Set(x => x.UseSsl, true));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "batch applied");

        // A 3-property batch must produce exactly one new emission, not three.
        Assert.Equal(baseline + 1, Volatile.Read(ref emissions));
    }

    [Fact]
    public async Task PatchAsync_SingleSet_Works()
    {
        var (_, storage, manager) = Build("{\"Host\":\"h\",\"Port\":25}");

        await storage.PatchAsync(b => b.Set(x => x.Port, 465));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 465, Timeout, description: "Port updated");

        Assert.Equal(465, manager.GetConfig<SmtpSettings>()!.Port);
        Assert.Equal("h", manager.GetConfig<SmtpSettings>()!.Host); // unchanged
    }

    [Fact]
    public async Task PatchAsync_WithReset_RemovesOverride()
    {
        var (_, storage, manager) = Build("{\"Host\":\"default.host\",\"Port\":25}");

        await storage.PatchAsync(b => b.Set(x => x.Port, 587));
        await ActiveWaitHelpers.WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "set");

        await storage.PatchAsync(b => b.Reset(x => x.Port));
        await ActiveWaitHelpers.WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 25, Timeout, description: "reset");

        Assert.Equal(25, manager.GetConfig<SmtpSettings>()!.Port);
    }

    [Fact]
    public async Task PatchAsync_MixedSetAndReset_InOneBatch()
    {
        var (_, storage, manager) = Build("{\"Host\":\"default.host\",\"Port\":25,\"UseSsl\":false}");

        await storage.SetAsync(x => x.Host, "overridden.host");
        await ActiveWaitHelpers.WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Host == "overridden.host", Timeout, description: "host set");

        await storage.PatchAsync(b => b
            .Reset(x => x.Host)        // back to default
            .Set(x => x.Port, 2525));  // override
        await ActiveWaitHelpers.WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 2525, Timeout, description: "mixed");

        var config = manager.GetConfig<SmtpSettings>()!;
        Assert.Equal("default.host", config.Host); // reset to inherited
        Assert.Equal(2525, config.Port);
    }

    [Fact]
    public async Task SetAsync_StillWorks_DelegatesToPatch()
    {
        var (_, storage, manager) = Build("{\"Host\":\"h\",\"Port\":25}");

        await storage.SetAsync(x => x.Port, 993);

        await ActiveWaitHelpers.WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 993, Timeout, description: "set");
        Assert.Equal(993, manager.GetConfig<SmtpSettings>()!.Port);
    }

    // ---------------------------------------------------------------- PatchAsync — async overload

    [Fact]
    public async Task PatchAsync_AsyncOverload_GathersValuesAsync()
    {
        var (_, storage, manager) = Build("{\"Host\":\"h\",\"Port\":25}");

        await storage.PatchAsync(async b =>
        {
            var port = await Task.FromResult(587); // stand-in for async work (e.g. encrypting a secret)
            b.Set(x => x.Port, port).Set(x => x.Host, "async.host");
        });

        await ActiveWaitHelpers.WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "async overload");

        var config = manager.GetConfig<SmtpSettings>()!;
        Assert.Equal(587, config.Port);
        Assert.Equal("async.host", config.Host);
    }

    [Fact]
    public async Task PatchAsync_ResetSecretPath_IsAllowed()
    {
        // Symmetry: a secret can be SET via the overlay, so it must also be resettable. Removing the override
        // is safe (no plaintext is written), so Reset on a secret member must NOT throw NotSupportedException.
        var file = TempFileHelper.Create("{\"Plain\":\"base\"}");
        _disposables.Add(file);
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => new ConfigRule[]
        {
            rules.For<SecretSettings>().FromFile(file.FilePath).Required(),
            rules.For<SecretSettings>().FromStore(new InMemoryBackend()),
        }));
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        var store = provider.GetRequiredService<IWritableStore<SecretSettings>>();

        var patchEx = await Record.ExceptionAsync(() => store.PatchAsync(b => b.Reset(x => x.ApiKey!)));
        Assert.Null(patchEx);

        var resetEx = await Record.ExceptionAsync(() => store.ResetAsync(x => x.ApiKey!));
        Assert.Null(resetEx);
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }
}
