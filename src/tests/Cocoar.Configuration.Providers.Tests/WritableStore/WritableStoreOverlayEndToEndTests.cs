using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.WritableStore;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.WritableStore;

[Trait("Type", "Unit")]
public sealed class WritableStoreOverlayEndToEndTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly List<IDisposable> _disposables = new();

    private (ServiceProvider Provider, IWritableStore<SmtpSettings> Storage, ConfigManager Manager) Build(
        string baseJson, InMemoryBackend? backend = null)
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

        var storage = provider.GetRequiredService<IWritableStore<SmtpSettings>>();
        var manager = provider.GetRequiredService<ConfigManager>();
        return (provider, storage, manager);
    }

    [Fact]
    public async Task Set_OverridesOnlyTouchedKey_OthersInherit_PascalCaseBase()
    {
        var (_, storage, manager) = Build("{\"Host\":\"smtp.default.com\",\"Port\":25,\"UseSsl\":false}");

        await storage.SetAsync(x => x.Port, 587);

        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "Port override");

        var config = manager.GetConfig<SmtpSettings>()!;
        Assert.Equal(587, config.Port);
        Assert.Equal("smtp.default.com", config.Host); // inherited
        Assert.False(config.UseSsl);                   // inherited
    }

    [Fact]
    public async Task Set_AlignsToCamelCaseBase_NoSiblingKey()
    {
        // Base file uses camelCase keys; the override must land on them (Trap B), not create PascalCase siblings.
        var (_, storage, manager) = Build("{\"host\":\"smtp.default.com\",\"port\":25}");

        await storage.SetAsync(x => x.Port, 587);

        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "camelCase override");

        var config = manager.GetConfig<SmtpSettings>()!;
        Assert.Equal(587, config.Port);
        Assert.Equal("smtp.default.com", config.Host); // still inherited (no ambiguous duplicate key)
    }

    [Fact]
    public async Task Reset_RestoresInheritedValue()
    {
        var (_, storage, manager) = Build("{\"Port\":25}");

        await storage.SetAsync(x => x.Port, 587);
        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "override applied");

        var removed = await storage.ResetAsync(x => x.Port);
        Assert.True(removed);

        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 25, Timeout, description: "reset to base");
    }

    [Fact]
    public async Task ExplicitNull_ClobbersBase_DistinctFromReset()
    {
        var (_, storage, manager) = Build("{\"Host\":\"smtp.default.com\"}");

        await storage.SetAsync(x => x.Host, null);
        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Host is null, Timeout, description: "explicit null clobber");

        await storage.ResetAsync(x => x.Host);
        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Host == "smtp.default.com", Timeout, description: "reset restores");
    }

    [Fact]
    public async Task SetToDefaultValue_StillOverrides()
    {
        // Base Port is 25; overriding to the C# default (0) must persist and win — the headline correctness win.
        var (_, storage, manager) = Build("{\"Port\":25}");

        await storage.SetAsync(x => x.Port, 0);

        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 0, Timeout, description: "default-valued override");
    }

    [Fact]
    public async Task Write_EmitsReactiveUpdate()
    {
        var (provider, storage, _) = Build("{\"Port\":25}");
        var reactive = provider.GetRequiredService<IReactiveConfig<SmtpSettings>>();

        SmtpSettings? latest = null;
        using var subscription = reactive.Subscribe(v => latest = v);

        await storage.SetAsync(x => x.Port, 587);

        await ActiveWaitHelpers.WaitUntilAsync(
            () => latest?.Port == 587, Timeout, description: "reactive emission");
    }

    [Fact]
    public async Task DescribeAsync_ReportsBaseEffectiveAndOverridden()
    {
        var (_, storage, manager) = Build("{\"Host\":\"smtp.default.com\",\"Port\":25}");

        await storage.SetAsync(x => x.Port, 587);
        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "override applied");

        var entries = await storage.DescribeAsync();

        var port = Assert.Single(entries, e => e.KeyPath == "Port");
        Assert.True(port.IsSet);
        Assert.Equal(25, port.BaseValue!.Value.GetInt32());
        Assert.Equal(587, port.EffectiveValue!.Value.GetInt32());

        var host = Assert.Single(entries, e => e.KeyPath == "Host");
        Assert.False(host.IsSet);
        Assert.Equal("smtp.default.com", host.BaseValue!.Value.GetString());
        Assert.Equal("smtp.default.com", host.EffectiveValue!.Value.GetString());
    }

    [Fact]
    public async Task ReadAsync_ReturnsSparsePartial_ReadOverlay_ReturnsRaw()
    {
        var (_, storage, _) = Build("{\"Host\":\"smtp.default.com\",\"Port\":25}");

        Assert.Null(await storage.ReadAsync()); // nothing overridden yet

        await storage.SetAsync(x => x.Port, 587);

        var partial = await storage.ReadAsync();
        Assert.NotNull(partial);
        Assert.Equal(587, partial!.Port);
        Assert.Null(partial.Host); // unset in the sparse overlay => C# default

        var raw = await storage.Overlay.ReadOverlayAsync();
        Assert.NotNull(raw);
        Assert.Equal(587, raw!["Port"]!.GetValue<int>());
    }

    [Fact]
    public async Task Clear_RemovesAllOverrides()
    {
        var (_, storage, manager) = Build("{\"Port\":25}");

        await storage.SetAsync(x => x.Port, 587);
        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 587, Timeout, description: "override applied");

        await storage.ClearAsync();
        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<SmtpSettings>()!.Port == 25, Timeout, description: "cleared to base");

        Assert.Null(await storage.ReadAsync());
    }

    [Fact]
    public void BothInterfaces_ResolveToSameSingletonInstance()
    {
        var (provider, storage, _) = Build("{\"Port\":25}");

        var overlay = provider.GetRequiredService<IWritableStoreOverlay<SmtpSettings>>();
        var storageAgain = provider.GetRequiredService<IWritableStore<SmtpSettings>>();

        Assert.Same(storage, storageAgain);              // singleton
        Assert.Same(storage.Overlay, overlay);           // overlay resolves to the same adapter
        Assert.True(ReferenceEquals(storage, overlay));  // one object implements both
    }

    public void Dispose()
    {
        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            try { _disposables[i].Dispose(); } catch { /* best effort */ }
        }
    }
}
