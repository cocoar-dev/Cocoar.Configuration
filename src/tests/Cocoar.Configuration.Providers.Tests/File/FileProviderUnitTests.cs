using System.Text.Json;
using Xunit;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Providers.Tests.File;

// File provider unit tests (deterministic file operations with TempFileHelper)
public class FileProviderUnitTests
{
    private sealed class AppConfig { public string? Name { get; set; } public int Value { get; set; } }
    private sealed class NestedConfig { public AppConfig App { get; set; } = new(); }

    private static ConfigRule CreateFileRule<T>(string filePath, string? selectPath = null, bool required = false)
    {
        var rulesBuilder = new RulesBuilder();
        var builder = rulesBuilder.For<T>().FromFile(filePath);
        if (selectPath != null) builder = builder.Select(selectPath);
        if (required) builder = builder.Required();
        return builder;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public void MissingFile_OptionalRule_SkipsHealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), "cocoar_missing_" + Guid.NewGuid().ToString("N") + ".json");
        var rule = CreateFileRule<object>(path, required: false); // explicitly optional
        using var manager = new ConfigManager(new[]{rule});
        
        // Optional rule should initialize successfully even with missing file
        manager.Initialize();
        var snap = manager.GetHealthService().Snapshot;
        
        // Optional rule fails but doesn't make the system unhealthy - shows as Down but overall Degraded
        Assert.Single(snap.Rules);
        Assert.Equal(Health.RuleResultStatus.Down, snap.Rules[0].Status);
        Assert.False(snap.Rules[0].Required); // Confirm it's optional
        Assert.Equal(Health.HealthStatus.Degraded, snap.OverallStatus); // Optional failure = Degraded
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public void MissingFile_RequiredRule_Degrades()
    {
        var path = Path.Combine(Path.GetTempPath(), "cocoar_missing_req_" + Guid.NewGuid().ToString("N") + ".json");
        var rule = CreateFileRule<object>(path, required: true);
        using var manager = new ConfigManager(new[]{rule});
        
        // Should throw during initialization for required missing file (wrapped in InvalidOperationException)
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Initialize());
        Assert.Contains("Required rule failed", ex.Message);
        Assert.IsType<FileNotFoundException>(ex.InnerException);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public void ValidJsonFile_LoadsCorrectly()
    {
        using var file = TempFileHelper.Create();
        file.WriteJson(new { Name = "TestApp", Value = 42 });

        var rule = CreateFileRule<AppConfig>(file.FilePath, required: true);
        using var manager = new ConfigManager(new[]{rule}).Initialize();
        
        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("TestApp", config!.Name);
        Assert.Equal(42, config.Value);
        
        var snap = manager.GetHealthService().Snapshot;
        Assert.Equal(Health.HealthStatus.Healthy, snap.OverallStatus);
        Assert.Equal(Health.RuleResultStatus.Up, snap.Rules[0].Status);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public void MalformedJson_RequiredRule_Degrades()
    {
        using var file = TempFileHelper.Create();
        file.WriteContent("{ invalid json syntax }");

        var rule = CreateFileRule<AppConfig>(file.FilePath, required: true);
        using var manager = new ConfigManager(new[]{rule});
        
        // Should throw during initialization due to JSON parse error (wrapped in InvalidOperationException)
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Initialize());
        Assert.Contains("Required rule failed", ex.Message);
        // Inner exception should be JSON parsing related
        Assert.True(ex.InnerException is JsonException || ex.InnerException?.GetType().Name.Contains("Json") == true);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public void EmptyJsonObject_LoadsAsDefault()
    {
        using var file = TempFileHelper.Create();
        file.WriteContent("{}");

        var rule = CreateFileRule<AppConfig>(file.FilePath, required: true);
        using var manager = new ConfigManager(new[]{rule}).Initialize();
        
        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Null(config!.Name); // Default value
        Assert.Equal(0, config.Value); // Default value
        
        var snap = manager.GetHealthService().Snapshot;
        Assert.Equal(Health.HealthStatus.Healthy, snap.OverallStatus);
        Assert.Equal(Health.RuleResultStatus.Up, snap.Rules[0].Status);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public void NestedJsonStructure_MapsToHierarchy()
    {
        using var file = TempFileHelper.Create();
        file.WriteJson(new { App = new { Name = "Nested", Value = 100 } });

        var rule = CreateFileRule<NestedConfig>(file.FilePath, required: true);
        using var manager = new ConfigManager(new[]{rule}).Initialize();
        
        var config = manager.GetConfig<NestedConfig>();
        Assert.NotNull(config);
        Assert.Equal("Nested", config!.App.Name);
        Assert.Equal(100, config.App.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public void FileWithSelect_ExtractsSection()
    {
        using var file = TempFileHelper.Create();
        file.WriteJson(new { 
            Database = new { ConnectionString = "test" },
            App = new { Name = "SectionTest", Value = 200 }
        });

        var rule = CreateFileRule<AppConfig>(file.FilePath, selectPath: "App", required: true);
        using var manager = new ConfigManager(new[]{rule}).Initialize();
        
        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("SectionTest", config!.Name);
        Assert.Equal(200, config.Value);
    }
}
