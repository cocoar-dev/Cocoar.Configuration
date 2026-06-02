using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.MicrosoftAdapter;
using Cocoar.Configuration.Providers.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.MicrosoftAdapter;

public class MicrosoftAdapterBattleTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try { disposable.Dispose(); } catch { /* ignore */ }
        }
    }

    #region New MicrosoftConfigurationProvider tests (IConfiguration-based)

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_ReadsFromIConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Name"] = "TestApp",
                ["App:Version"] = "1.0.0",
                ["Database:ConnectionString"] = "Server=localhost",
                ["Database:Timeout"] = "30"
            })
            .Build();

        var provider = new MicrosoftConfigurationProvider(
            new MicrosoftConfigurationProviderOptions(configuration));

        var result = await provider.FetchConfigurationBytesAsync(
            new MicrosoftConfigurationProviderQueryOptions());

        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);

        var app = result.ToJsonElement().GetProperty("App");
        Assert.Equal("TestApp", app.GetProperty("Name").GetString());
        Assert.Equal("1.0.0", app.GetProperty("Version").GetString());

        var db = result.ToJsonElement().GetProperty("Database");
        Assert.Equal("Server=localhost", db.GetProperty("ConnectionString").GetString());
        Assert.Equal("30", db.GetProperty("Timeout").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public void ConfigManager_FiltersWithSelect()
    {
        // Section filtering is now handled at the rule level via .Select(), not at the provider level
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Name"] = "TestApp",
                ["Database:ConnectionString"] = "Server=localhost",
                ["Logging:Level"] = "Debug",
                ["Logging:Providers:Console"] = "true"
            })
            .Build();

        using var manager = ConfigManager.Create(c => c.UseConfiguration(
            rules => [rules.For<LoggingConfig>().FromIConfiguration(configuration).Select("Logging")]));

        var config = manager.GetConfig<LoggingConfig>();

        Assert.NotNull(config);
        Assert.Equal("Debug", config.Level);
        Assert.NotNull(config.Providers);
        Assert.True(config.Providers.Console);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_HandlesEmptyIConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var provider = new MicrosoftConfigurationProvider(
            new MicrosoftConfigurationProviderOptions(configuration));

        var result = await provider.FetchConfigurationBytesAsync(
            new MicrosoftConfigurationProviderQueryOptions());

        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        Assert.Equal("{}", result.ToJsonElement().GetRawText());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public void ConfigManager_HandlesNonExistentSection_ViaSelect()
    {
        // When .Select() targets a non-existent section, the rule produces defaults
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Name"] = "TestApp"
            })
            .Build();

        using var manager = ConfigManager.Create(c => c.UseConfiguration(
            rules => [rules.For<AppConfig>().FromIConfiguration(configuration).Select("NonExistent")]));

        var config = manager.GetConfig<AppConfig>();

        // Non-existent section yields default values
        Assert.NotNull(config);
        Assert.Equal("", config.Name);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_HandlesComplexNesting_IConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:Database:Primary:Host"] = "db1.example.com",
                ["Services:Database:Primary:Port"] = "5432",
                ["Services:Database:Secondary:Host"] = "db2.example.com",
                ["Services:Cache:Redis:Endpoints:0"] = "redis1.example.com",
                ["Services:Cache:Redis:Endpoints:1"] = "redis2.example.com"
            })
            .Build();

        var provider = new MicrosoftConfigurationProvider(
            new MicrosoftConfigurationProviderOptions(configuration));

        var result = await provider.FetchConfigurationBytesAsync(
            new MicrosoftConfigurationProviderQueryOptions());

        var services = result.ToJsonElement().GetProperty("Services");

        var primaryDb = services.GetProperty("Database").GetProperty("Primary");
        Assert.Equal("db1.example.com", primaryDb.GetProperty("Host").GetString());
        Assert.Equal("5432", primaryDb.GetProperty("Port").GetString());

        var secondaryDb = services.GetProperty("Database").GetProperty("Secondary");
        Assert.Equal("db2.example.com", secondaryDb.GetProperty("Host").GetString());

        var redisEndpoints = services.GetProperty("Cache").GetProperty("Redis").GetProperty("Endpoints");
        Assert.Equal("redis1.example.com", redisEndpoints.GetProperty("0").GetString());
        Assert.Equal("redis2.example.com", redisEndpoints.GetProperty("1").GetString());
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Provider", "MicrosoftAdapter")]
    public void ConfigManager_IntegratesWithIConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Name"] = "IntegrationTest",
                ["App:Features:EnableCache"] = "true",
                ["App:Features:MaxConnections"] = "100"
            })
            .Build();

        using var manager = ConfigManager.Create(c => c.UseConfiguration(
            rules => [rules.For<AppConfig>().FromIConfiguration(configuration).Select("App")]));

        var config = manager.GetConfig<AppConfig>();

        Assert.NotNull(config);
        Assert.Equal("IntegrationTest", config.Name);
        Assert.NotNull(config.Features);
        Assert.True(config.Features.EnableCache);
        Assert.Equal(100, config.Features.MaxConnections);

        Assert.Equal(Health.HealthStatus.Healthy, manager.HealthStatus);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public void ConfigManager_IntegratesWithIConfiguration_NoSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Name"] = "DirectTest",
                ["Features:EnableCache"] = "false",
                ["Features:MaxConnections"] = "50"
            })
            .Build();

        using var manager = ConfigManager.Create(c => c.UseConfiguration(
            rules => [rules.For<AppConfig>().FromIConfiguration(configuration)]));

        var config = manager.GetConfig<AppConfig>();

        Assert.NotNull(config);
        Assert.Equal("DirectTest", config.Name);
        Assert.NotNull(config.Features);
        Assert.False(config.Features.EnableCache);
        Assert.Equal(50, config.Features.MaxConnections);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_AcceptsIConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Name"] = "SectionTest",
                ["App:Version"] = "2.0",
                ["Other:Ignored"] = "yes"
            })
            .Build();

        // Pass an IConfigurationSection directly as the IConfiguration
        var section = configuration.GetSection("App");
        var provider = new MicrosoftConfigurationProvider(
            new MicrosoftConfigurationProviderOptions(section));

        var result = await provider.FetchConfigurationBytesAsync(
            new MicrosoftConfigurationProviderQueryOptions());

        Assert.Equal("SectionTest", result.ToJsonElement().GetProperty("Name").GetString());
        Assert.Equal("2.0", result.ToJsonElement().GetProperty("Version").GetString());
        // The "Other" section should not appear since we scoped to "App"
        Assert.False(result.ToJsonElement().TryGetProperty("Other", out _));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task ChangeDetection_FiresOnReload()
    {
        var data = new Dictionary<string, string?>
        {
            ["Value"] = "original"
        };
        var source = new MemoryConfigurationSource { InitialData = data };
        var configuration = new ConfigurationBuilder()
            .Add(source)
            .Build();

        var provider = new MicrosoftConfigurationProvider(
            new MicrosoftConfigurationProviderOptions(configuration));

        var tcs = new TaskCompletionSource<byte[]>();
        using var sub = provider
            .ChangesAsBytes(new MicrosoftConfigurationProviderQueryOptions())
            .Subscribe(new DelegateObserver<byte[]>(bytes => tcs.TrySetResult(bytes)));

        // Trigger a reload on the IConfigurationRoot
        configuration.Reload();

        var changedBytes = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(JsonValueKind.Object, changedBytes.ToJsonElement().ValueKind);
        Assert.Equal("original", changedBytes.ToJsonElement().GetProperty("Value").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task ChangeDetection_FiresOnReload_FullDocument()
    {
        // Section filtering is now handled at the rule level via .Select(),
        // so the provider always emits the full document on reload.
        var data = new Dictionary<string, string?>
        {
            ["App:Value"] = "initial",
            ["Other:Ignored"] = "yes"
        };
        var source = new MemoryConfigurationSource { InitialData = data };
        var configuration = new ConfigurationBuilder()
            .Add(source)
            .Build();

        var provider = new MicrosoftConfigurationProvider(
            new MicrosoftConfigurationProviderOptions(configuration));

        var tcs = new TaskCompletionSource<byte[]>();
        using var sub = provider
            .ChangesAsBytes(new MicrosoftConfigurationProviderQueryOptions())
            .Subscribe(new DelegateObserver<byte[]>(bytes => tcs.TrySetResult(bytes)));

        // Trigger a reload
        configuration.Reload();

        var changedBytes = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("initial", changedBytes.ToJsonElement().GetProperty("App").GetProperty("Value").GetString());
        Assert.Equal("yes", changedBytes.ToJsonElement().GetProperty("Other").GetProperty("Ignored").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public void ProviderOptions_GenerateProviderKey_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder().Build();
        var options = new MicrosoftConfigurationProviderOptions(configuration);

        // Each rule should get its own provider instance
        Assert.Null(((Cocoar.Configuration.Providers.Abstractions.IProviderConfiguration)options).GenerateProviderKey());
    }

    #endregion

    #region Test Support Types

    private class AppConfig
    {
        public string Name { get; set; } = "";
        public FeaturesConfig Features { get; set; } = new();
    }

    private class FeaturesConfig
    {
        public bool EnableCache { get; set; }
        public int MaxConnections { get; set; }
    }

    private class LoggingConfig
    {
        public string Level { get; set; } = "";
        public LoggingProvidersConfig Providers { get; set; } = new();
    }

    private class LoggingProvidersConfig
    {
        public bool Console { get; set; }
    }

    /// <summary>
    /// Minimal IObserver implementation for testing change detection.
    /// </summary>
    private sealed class DelegateObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }

    #endregion
}
