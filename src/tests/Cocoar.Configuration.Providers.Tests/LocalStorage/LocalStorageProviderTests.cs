using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.LocalStorage;

[Trait("Type", "Unit")]
[Trait("Provider", "LocalStorageProvider")]
public class LocalStorageProviderTests : IDisposable
{
    private sealed class AppConfig
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    private readonly string _testDir;

    public LocalStorageProviderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "cocoar_provider_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    private IStorageBackend CreateBackend() => new FileStorageBackend(_testDir);

    [Fact]
    public void FromLocalStorage_CreatesValidRule()
    {
        var rulesBuilder = new RulesBuilder();
        ConfigRule rule = rulesBuilder.For<AppConfig>().FromLocalStorage(CreateBackend());

        Assert.Equal(typeof(LocalStorageProvider), rule.ProviderType);
        Assert.Equal(typeof(AppConfig), rule.ConcreteType);
    }

    [Fact]
    public void ConfigManager_WithLocalStorage_InitializesSuccessfully()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<AppConfig>().FromLocalStorage(CreateBackend())
            ]));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        // Default values since nothing written yet
        Assert.Null(config.Name);
        Assert.Equal(0, config.Value);
    }

    [Fact]
    public async Task ConfigManager_WithLocalStorage_LoadsPersistedData()
    {
        var backend = CreateBackend();
        // Pre-persist data
        await backend.WriteAsync(
            typeof(AppConfig).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(new AppConfig { Name = "Persisted", Value = 99 })
        );

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<AppConfig>().FromLocalStorage(backend)
            ]));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("Persisted", config.Name);
        Assert.Equal(99, config.Value);
    }

    [Fact]
    public async Task ConfigManager_WithLocalStorage_MergesWithFileDefaults()
    {
        var backend = CreateBackend();
        // Pre-persist partial override
        await backend.WriteAsync(
            typeof(AppConfig).FullName!,
            """{"Name":"Override"}"""u8.ToArray()
        );

        // Create a temp file with defaults
        var tempFile = Path.Combine(_testDir, "defaults.json");
        System.IO.File.WriteAllText(tempFile, """{"Name":"Default","Value":42}""");

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<AppConfig>().FromFile(tempFile),
                rules.For<AppConfig>().FromLocalStorage(backend)  // Higher prio
            ]));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("Override", config.Name);   // From LocalStorage
        Assert.Equal(42, config.Value);           // From File (not overridden)
    }

    [Fact]
    public async Task WriteToStore_TriggersRecompute()
    {
        var backend = CreateBackend();
        var storageKey = typeof(AppConfig).FullName!;
        var store = new LocalStorageStore(backend, storageKey)
        {
            ConfigurationType = typeof(AppConfig)
        };

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration([
                new ConfigRule(
                    typeof(LocalStorageProvider),
                    new LocalStorageProviderOptions(store),
                    LocalStorageProviderQueryOptions.Default,
                    typeof(AppConfig))
            ])
            .UseDebounce(50));

        var reactiveConfig = manager.GetReactiveConfig<AppConfig>();

        // Initial state
        Assert.Null(reactiveConfig.CurrentValue.Name);

        // Write new config
        var newConfig = new AppConfig { Name = "Updated", Value = 100 };
        await store.WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(newConfig));

        // Wait for recompute (debounce + processing)
        await WaitUntilAsync(
            () => reactiveConfig.CurrentValue.Name == "Updated",
            description: "reactive config to reflect written value");

        Assert.Equal("Updated", reactiveConfig.CurrentValue.Name);
        Assert.Equal(100, reactiveConfig.CurrentValue.Value);
    }

    [Fact]
    public async Task LocalStorageAdapter_WriteAsync_TriggersRecompute()
    {
        var backend = CreateBackend();
        var storageKey = typeof(AppConfig).FullName!;
        var store = new LocalStorageStore(backend, storageKey)
        {
            ConfigurationType = typeof(AppConfig)
        };

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration([
                new ConfigRule(
                    typeof(LocalStorageProvider),
                    new LocalStorageProviderOptions(store),
                    LocalStorageProviderQueryOptions.Default,
                    typeof(AppConfig))
            ])
            .UseDebounce(50));

        var reactiveConfig = manager.GetReactiveConfig<AppConfig>();
        var localStorage = new LocalStorageAdapter<AppConfig>(store);

        // Write via adapter (same as ILocalStorage<T>.WriteAsync)
        await localStorage.WriteAsync(new AppConfig { Name = "ViaAdapter", Value = 77 });

        await WaitUntilAsync(
            () => reactiveConfig.CurrentValue.Name == "ViaAdapter",
            description: "reactive config to reflect adapter write");

        Assert.Equal("ViaAdapter", reactiveConfig.CurrentValue.Name);
        Assert.Equal(77, reactiveConfig.CurrentValue.Value);
    }

    [Fact]
    public void ConfigAwareFactory_ReceivesAccessor()
    {
        var backend = CreateBackend();

        // Pre-persist base settings so accessor has something to read
        var baseFile = Path.Combine(_testDir, "base.json");
        System.IO.File.WriteAllText(baseFile, """{"Name":"Base","Value":1}""");

        IConfigurationAccessor? capturedAccessor = null;

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<AppConfig>().FromFile(baseFile),
                rules.For<AppConfig>().FromLocalStorage((accessor, _) =>
                {
                    capturedAccessor = accessor;
                    return backend;
                })
            ]));

        Assert.NotNull(capturedAccessor);
        // The accessor should be able to read the config from the earlier rule
        var config = capturedAccessor.GetConfig<AppConfig>();
        Assert.NotNull(config);
    }

    [Fact]
    public async Task DuplicateFromLocalStorage_LastRuleWins()
    {
        var backend1 = new FileStorageBackend(Path.Combine(_testDir, "store1"));
        var backend2 = new FileStorageBackend(Path.Combine(_testDir, "store2"));

        // Pre-persist different values
        await backend1.WriteAsync(typeof(AppConfig).FullName!, """{"Name":"First"}"""u8.ToArray());
        await backend2.WriteAsync(typeof(AppConfig).FullName!, """{"Name":"Second"}"""u8.ToArray());

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<AppConfig>().FromLocalStorage(backend1),
                rules.For<AppConfig>().FromLocalStorage(backend2),
            ]));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("Second", config.Name);  // Last rule wins
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout = default,
        string description = "condition")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try { if (condition()) return; } catch { }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Timeout waiting for {description} after {timeout}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}
