using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.LocalStorage;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Reactive;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

[Trait("Type", "Unit")]
[Trait("Component", "DI")]
public class LocalStorageDITests : IDisposable
{
    private sealed class AppSettings
    {
        public string? AppName { get; set; }
        public bool FeatureEnabled { get; set; }
    }

    private sealed class DbSettings
    {
        public string? ConnectionString { get; set; }
        public int Timeout { get; set; }
    }

    private readonly string _testDir;

    public LocalStorageDITests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "cocoar_di_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    private IStorageBackend CreateBackend() => new FileStorageBackend(_testDir);

    [Fact]
    public void ILocalStorage_IsRegistered_WhenFromLocalStorageUsed()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ]));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetService<ILocalStorage<AppSettings>>();

        Assert.NotNull(localStorage);
    }

    [Fact]
    public void ILocalStorage_IsNotRegistered_WhenNotUsed()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromStaticJson("""{"AppName":"Test"}""")
            ]));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetService<ILocalStorage<AppSettings>>();

        Assert.Null(localStorage);
    }

    [Fact]
    public void MultipleTypes_EachGetOwnLocalStorage()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend()),
                rules.For<DbSettings>().FromLocalStorage(CreateBackend())
            ]));

        var sp = services.BuildServiceProvider();
        var appStorage = sp.GetService<ILocalStorage<AppSettings>>();
        var dbStorage = sp.GetService<ILocalStorage<DbSettings>>();

        Assert.NotNull(appStorage);
        Assert.NotNull(dbStorage);
        Assert.NotSame(appStorage, dbStorage);
    }

    [Fact]
    public async Task WriteViaLocalStorage_UpdatesReactiveConfig()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ])
            .UseDebounce(50));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();
        var reactiveConfig = sp.GetRequiredService<IReactiveConfig<AppSettings>>();

        // Initial: defaults
        Assert.Null(reactiveConfig.CurrentValue.AppName);

        // Write
        await localStorage.WriteAsync(new AppSettings
        {
            AppName = "MyApp",
            FeatureEnabled = true
        });

        // Wait for recompute
        await WaitUntilAsync(
            () => reactiveConfig.CurrentValue.AppName == "MyApp",
            description: "reactive config to update after write");

        Assert.Equal("MyApp", reactiveConfig.CurrentValue.AppName);
        Assert.True(reactiveConfig.CurrentValue.FeatureEnabled);
    }

    [Fact]
    public async Task WriteViaLocalStorage_PersistsAcrossResolves()
    {
        var backend = CreateBackend();
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(backend)
            ])
            .UseDebounce(50));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();

        await localStorage.WriteAsync(new AppSettings { AppName = "Persisted" });

        // Wait for recompute
        var reactiveConfig = sp.GetRequiredService<IReactiveConfig<AppSettings>>();
        await WaitUntilAsync(
            () => reactiveConfig.CurrentValue.AppName == "Persisted",
            description: "config to persist");

        // Verify file exists in backend
        var stored = await backend.ReadAsync(typeof(AppSettings).FullName!);
        Assert.NotNull(stored);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(stored);
        Assert.Equal("Persisted", deserialized?.AppName);
    }

    [Fact]
    public async Task ReadAsync_NothingStored_ReturnsNull()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ]));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();

        var result = await localStorage.ReadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_AfterWrite_ReturnsStoredValue()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ])
            .UseDebounce(50));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();

        await localStorage.WriteAsync(new AppSettings { AppName = "Stored", FeatureEnabled = true });

        var result = await localStorage.ReadAsync();
        Assert.NotNull(result);
        Assert.Equal("Stored", result.AppName);
        Assert.True(result.FeatureEnabled);
    }

    [Fact]
    public async Task ReadAsync_ReturnRawStoreValue_NotMergedPipeline()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                // File provides both AppName and FeatureEnabled
                rules.For<AppSettings>().FromStaticJson("""{"AppName":"FromFile","FeatureEnabled":true}"""),
                // LocalStorage only overrides AppName
                rules.For<AppSettings>().FromLocalStorage(CreateBackend()),
            ])
            .UseDebounce(50));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();
        var reactiveConfig = sp.GetRequiredService<IReactiveConfig<AppSettings>>();

        await localStorage.WriteAsync(new AppSettings { AppName = "Override" });

        await WaitUntilAsync(
            () => reactiveConfig.CurrentValue.AppName == "Override",
            description: "reactive config to update");

        // Merged pipeline: LocalStorage wins (last-rule-wins) for all properties it sets.
        // Since WriteAsync serialized the full object, FeatureEnabled=false overwrites the static true.
        Assert.Equal("Override", reactiveConfig.CurrentValue.AppName);
        Assert.False(reactiveConfig.CurrentValue.FeatureEnabled);

        // Raw store: exactly what was written
        var stored = await localStorage.ReadAsync();
        Assert.NotNull(stored);
        Assert.Equal("Override", stored.AppName);
        Assert.False(stored.FeatureEnabled);

        // ReadAsync returns the raw store value, NOT the merged pipeline.
        // The static rule's FeatureEnabled=true is only visible via IReactiveConfig
        // when LocalStorage hasn't overridden it.
    }

    [Fact]
    public async Task UpdateAsync_ModifiesSingleProperty()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ])
            .UseDebounce(50));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();

        // Write initial state
        await localStorage.WriteAsync(new AppSettings { AppName = "MyApp", FeatureEnabled = false });

        // Update only one property
        await localStorage.UpdateAsync(s => s.FeatureEnabled = true);

        var stored = await localStorage.ReadAsync();
        Assert.NotNull(stored);
        Assert.Equal("MyApp", stored.AppName);       // Preserved
        Assert.True(stored.FeatureEnabled);            // Updated
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentUpdates_AreSerialized()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ])
            .UseDebounce(50));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();

        // Write initial
        await localStorage.WriteAsync(new AppSettings { AppName = "Start" });

        // 10 concurrent updates each append to AppName
        var tasks = Enumerable.Range(0, 10)
            .Select(i => localStorage.UpdateAsync(s => s.AppName += $"_{i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        var result = await localStorage.ReadAsync();
        Assert.NotNull(result);

        // All 10 updates should be present (order may vary but all appended)
        for (var i = 0; i < 10; i++)
            Assert.Contains($"_{i}", result.AppName);
    }

    [Fact]
    public async Task UpdateAsync_NothingStored_StartsFromDefaults()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ])
            .UseDebounce(50));

        var sp = services.BuildServiceProvider();
        var localStorage = sp.GetRequiredService<ILocalStorage<AppSettings>>();

        // Update without prior write — starts from default-constructed AppSettings
        await localStorage.UpdateAsync(s => s.AppName = "FromUpdate");

        var stored = await localStorage.ReadAsync();
        Assert.NotNull(stored);
        Assert.Equal("FromUpdate", stored.AppName);
        Assert.False(stored.FeatureEnabled);  // Default
    }

    [Fact]
    public void ILocalStorage_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppSettings>().FromLocalStorage(CreateBackend())
            ]));

        var sp = services.BuildServiceProvider();
        var instance1 = sp.GetRequiredService<ILocalStorage<AppSettings>>();
        var instance2 = sp.GetRequiredService<ILocalStorage<AppSettings>>();

        Assert.Same(instance1, instance2);
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
