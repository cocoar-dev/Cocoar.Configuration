using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Cocoar.Configuration.Fluent;

using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Tests.Providers;

public class FileSourceProviderTests
{
    [Fact]
    public async Task FetchConfigurationAsync_ReturnsSection_WhenJsonFileExists()
    {
        // Arrange
        var tempPath = Path.GetFullPath(Path.Combine("TestConfigFiles", "config1.json"));
        var provider = new FileSourceProvider(new FileSourceProviderOptions(Path.GetDirectoryName(tempPath)!));

        // Act
        var result = await provider.FetchConfigurationAsync( new FileSourceProviderQueryOptions(Path.GetFileName(tempPath),"SectionA"));

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
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath, "SectionA")).For<TestClass>().As<IMySectionSettings>()
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
    public void FileProvider_Merge_TwoFiles()
    {
        // Arrange
        var config1 = Path.GetFullPath(Path.Combine("TestConfigFiles", "config1.json"));
        var config2 = Path.GetFullPath(Path.Combine("TestConfigFiles", "config2.json"));
        
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(config1, "SectionA")).For<TestClass>(),
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(config2, "SectionA")).For<TestClass>(),
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

    [Fact]
    public async Task Changes_DoesNotEmit_OnSubscribe()
    {
        // arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(tempPath, "{ \"SectionA\": { \"Enabled\": true } }");
        var provider = new FileSourceProvider(new FileSourceProviderOptions(Path.GetDirectoryName(tempPath)!));

        // give watcher a moment to attach if created during provider construction
        await Task.Delay(50);

        var emitted = false;
        using var sub = provider
            .Changes(new FileSourceProviderQueryOptions(Path.GetFileName(tempPath), "SectionA"))
            .Subscribe(_ => emitted = true);

        // assert: no initial emission
        await Task.Delay(100);
        Assert.False(emitted);

        // cleanup
        File.Delete(tempPath);
    }
    
}

public class TestClass: FileSourceProviderTests.IMySectionSettings
{
    public bool Enabled { get; set; }
    public int Value { get; set; } = 2;
    public string StringValue { get; set; } = "Leer";
}
