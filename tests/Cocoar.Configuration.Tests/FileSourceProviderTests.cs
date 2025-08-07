using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Extensions.Tests;

public class FileSourceProviderTests
{
    [Fact]
    public async Task GetValueAsync_ReturnsSection_WhenJsonFileExists()
    {
        // Arrange
        var tempPath = Path.GetFullPath(Path.Combine("TestConfigFiles", "config1.json"));
        var provider = new FileSourceProvider(new FileSourceProviderOptions(Path.GetDirectoryName(tempPath)!));

        // Act
        var result = await provider.GetValueAsync( new FileSourceProviderQueryOptions(Path.GetFileName(tempPath),"SectionA"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.True(result.TryGetProperty("Enabled", out var enabled));
        Assert.Equal(JsonValueKind.True, enabled.ValueKind);
        
    }
    
    [Fact]
    public async Task FileProvider_Notification_Fires_OnFileChange()
    {
        // arrange ───────────────────────────────────────────────────────────────
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(tempPath, @"{ ""SectionA"": { ""Enabled"": true } }");

        // shorten debounce for faster tests (100 ms is plenty)
        var provider = new FileSourceProvider(new FileSourceProviderOptions(Path.GetDirectoryName(tempPath)!, TimeSpan.FromMilliseconds(100)));

        // ① subscribe (starts watcher)  ② convert to Task (actually subscribes)
        var changeTask = provider
            .Changes(new FileSourceProviderQueryOptions(Path.GetFileName(tempPath), "SectionA"))
            .FirstAsync()
            .Timeout(TimeSpan.FromSeconds(5)) // fail fast if it never comes
            .ToTask();

        // give the watcher a moment to hook into the kernel queue
        await Task.Delay(50);

        // act ───────────────────────────────────────────────────────────────────
        File.WriteAllText(tempPath,
            @"{ ""SectionA"": { ""Enabled"": false } }");

        // assert ────────────────────────────────────────────────────────────────
        var notification = await changeTask;   // will complete well < 5 s
        Assert.NotNull(notification);
        Assert.Equal(JsonValueKind.False,
            notification.GetProperty("Enabled").ValueKind);

        // cleanup
        File.Delete(tempPath);
    }

    [Fact]
    public async Task ConfigManager_ReturnsConfigFromFileProvider()
    {


        var tempPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempPath, @"{ ""SectionA"": { ""Enabled"": true } }");

            var services = new ServiceCollection();
            services.AddCocoarConfiguration([
                FileSourceProvider.CreateRule<TestClass, IMySectionSettings>(tempPath,"SectionA"),
            ]);
            
            var sp = services.BuildServiceProvider();
            
            var manager = sp.GetRequiredService<ConfigManager>();

            var result = manager.GetConfig<IMySectionSettings>();
            Assert.NotNull(result);
            Assert.True(result.Enabled);
            
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task FileProvider_Merge_TwoFiles()
    {
        // Arrange
        var config1 = Path.GetFullPath(Path.Combine("TestConfigFiles", "config1.json"));
        var config2 = Path.GetFullPath(Path.Combine("TestConfigFiles", "config2.json"));
        
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([
            FileSourceProvider.CreateRule<TestClass>(config1, "SectionA"),
            FileSourceProvider.CreateRule<TestClass>(config2, "SectionA"),
        ]);

        var sp = services.BuildServiceProvider();
        
        var manager = sp.GetRequiredService<ConfigManager>();
        var result = manager.GetConfig<TestClass>();
        
        
        // Assert
        Assert.NotNull(result);
        
        // Merge logic would go here, for now just check both are loaded
        Assert.Equal(false, result.Enabled);
        Assert.Equal(42, result.Value);
        Assert.Equal("Leer", result.StringValue);

    }

    public interface IMySectionSettings
    {
        bool Enabled { get; }
    }
    
}

public class TestClass: FileSourceProviderTests.IMySectionSettings
{
    public bool Enabled { get; set; }
    public int Value { get; set; } = 2;
    public string StringValue { get; set; } = "Leer";
}