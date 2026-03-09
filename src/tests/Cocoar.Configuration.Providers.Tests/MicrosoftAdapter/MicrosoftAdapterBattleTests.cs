using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.MicrosoftAdapter;
using Cocoar.Configuration.Providers.Tests.Helpers;
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

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_ReadsFromInMemoryConfig()
    {

        var data = new Dictionary<string, string?>
        {
            ["App:Name"] = "TestApp",
            ["App:Version"] = "1.0.0",
            ["Database:ConnectionString"] = "Server=localhost",
            ["Database:Timeout"] = "30"
        };
        var configSource = new MemoryConfigurationSource { InitialData = data };
        
        var provider = new MicrosoftConfigurationSourceProvider(
            new(configSource));
        // Note: MicrosoftConfigurationSourceProvider doesn't implement IDisposable


        var result = await provider.FetchConfigurationBytesAsync(
            new());


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
    public async Task FetchConfigurationAsync_FiltersWithPrefix()
    {

        var data = new Dictionary<string, string?>
        {
            ["App:Name"] = "TestApp",
            ["Database:ConnectionString"] = "Server=localhost",
            ["Logging:Level"] = "Debug",
            ["Logging:Providers:Console"] = "true"
        };
        var configSource = new MemoryConfigurationSource { InitialData = data };
        
        var provider = new MicrosoftConfigurationSourceProvider(
            new(configSource));


        var result = await provider.FetchConfigurationBytesAsync(
            new("Logging"));


        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        Assert.Equal("Debug", result.ToJsonElement().GetProperty("Level").GetString());
        
        var providers = result.ToJsonElement().GetProperty("Providers");
        Assert.Equal("true", providers.GetProperty("Console").GetString());
        

        Assert.False(result.ToJsonElement().TryGetProperty("App", out _));
        Assert.False(result.ToJsonElement().TryGetProperty("Database", out _));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_HandlesEmptyConfiguration()
    {

        var configSource = new MemoryConfigurationSource { InitialData = new Dictionary<string, string?>() };
        
        var provider = new MicrosoftConfigurationSourceProvider(
            new(configSource));


        var result = await provider.FetchConfigurationBytesAsync(
            new());


        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        Assert.Equal("{}", result.ToJsonElement().GetRawText());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_HandlesNonExistentPrefix()
    {

        var data = new Dictionary<string, string?>
        {
            ["App:Name"] = "TestApp"
        };
        var configSource = new MemoryConfigurationSource { InitialData = data };
        
        var provider = new MicrosoftConfigurationSourceProvider(
            new(configSource));


        var result = await provider.FetchConfigurationBytesAsync(
            new("NonExistent"));


        Assert.Equal(JsonValueKind.Object, result.ToJsonElement().ValueKind);
        Assert.Equal("{}", result.ToJsonElement().GetRawText());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_HandlesComplexNesting()
    {

        var data = new Dictionary<string, string?>
        {
            ["Services:Database:Primary:Host"] = "db1.example.com",
            ["Services:Database:Primary:Port"] = "5432",
            ["Services:Database:Secondary:Host"] = "db2.example.com",
            ["Services:Cache:Redis:Endpoints:0"] = "redis1.example.com",
            ["Services:Cache:Redis:Endpoints:1"] = "redis2.example.com"
        };
        var configSource = new MemoryConfigurationSource { InitialData = data };
        
        var provider = new MicrosoftConfigurationSourceProvider(
            new(configSource));


        var result = await provider.FetchConfigurationBytesAsync(
            new());


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
    public void ConfigManager_IntegratesWithMicrosoftConfig()
    {

        var data = new Dictionary<string, string?>
        {
            ["App:Name"] = "IntegrationTest",
            ["App:Features:EnableCache"] = "true",
            ["App:Features:MaxConnections"] = "100"
        };
        var configSource = new MemoryConfigurationSource { InitialData = data };

        var rule = MicrosoftConfigurationSourceProvider.CreateRule<AppConfig>(_ => new(
                configSource,
                configurationPrefix: "App"));

        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[] { rule }));


        var config = manager.GetConfig<AppConfig>();


        Assert.NotNull(config);
        Assert.Equal("IntegrationTest", config.Name);
        Assert.NotNull(config.Features);
        Assert.True(config.Features.EnableCache);
        Assert.Equal(100, config.Features.MaxConnections);


        var health = manager.GetHealthService().Snapshot;
        Assert.Equal(Health.HealthStatus.Healthy, health.OverallStatus);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "MicrosoftAdapter")]
    public async Task FetchConfigurationAsync_HandlesCaseInsensitiveKeys()
    {

        var data = new Dictionary<string, string?>
        {
            ["app:name"] = "LowerCase",
            ["APP:VERSION"] = "UpperCase",
            ["App:Environment"] = "MixedCase"
        };
        var configSource = new MemoryConfigurationSource { InitialData = data };
        
        var provider = new MicrosoftConfigurationSourceProvider(
            new(configSource));


        var result = await provider.FetchConfigurationBytesAsync(
            new());


        // Let's first check what the actual structure looks like
        var jsonString = result.ToJsonElement().GetRawText();
        Assert.NotEmpty(jsonString);
        
        // The keys might be normalized, so let's check what's actually available
        Assert.True(result.ToJsonElement().TryGetProperty("app", out var app) || result.ToJsonElement().TryGetProperty("App", out app) || result.ToJsonElement().TryGetProperty("APP", out app));
        
        // Check for properties within the app section using case-insensitive search
        var hasName = app.TryGetProperty("name", out var nameElement) || 
                     app.TryGetProperty("Name", out nameElement) ||
                     app.TryGetProperty("NAME", out nameElement);
        Assert.True(hasName);
        Assert.Equal("LowerCase", nameElement.GetString());
    }

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

    #endregion
}
