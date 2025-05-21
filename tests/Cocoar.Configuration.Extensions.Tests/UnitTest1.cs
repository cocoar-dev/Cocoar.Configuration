using System.IO;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using System.Threading.Tasks;
using Cocoar.Configuration.Extensions;
using Xunit;

public class FileConfigSourceProviderTests
{
    [Fact]
    public async Task GetValueAsync_ReturnsSection_WhenJsonFileExists()
    {
        // Arrange
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, @"{ ""SectionA"": { ""Enabled"": true } }");
        var provider = new FileConfigSourceProvider(tempPath);

        // Act
        var result = await provider.GetValueAsync("SectionA");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Object, result!.Value.ValueKind);
        Assert.True(result.Value.TryGetProperty("Enabled", out var enabled));
        Assert.Equal(JsonValueKind.True, enabled.ValueKind);

        // Cleanup

        File.Delete(tempPath);
    }
    
    [Fact]
    public async Task FileProvider_Notification_Fires_OnFileChange()
    {
        // arrange ───────────────────────────────────────────────────────────────
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(tempPath, @"{ ""SectionA"": { ""Enabled"": true } }");

        // shorten debounce for faster tests (100 ms is plenty)
        var provider = new FileConfigSourceProvider(tempPath,
            debounce: TimeSpan.FromMilliseconds(100));

        // ① subscribe (starts watcher)  ② convert to Task (actually subscribes)
        var changeTask = provider
            .Changes("SectionA")
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
            notification.NewValue!.Value.GetProperty("Enabled").ValueKind);

        // cleanup
        File.Delete(tempPath);
    }
    
    [Fact]
    public async Task ConfigManager_ReturnsConfigFromFileProvider()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, @"{ ""SectionA"": { ""Enabled"": true } }");
            var provider = new FileConfigSourceProvider(tempPath);

            var manager = new ConfigManager([
                new ConfigRule<FileConfigSourceProvider>("file1", typeof(IMySectionSettings), "SectionA")
            ]);
            // manager.RegisterProvider("file1", provider);

            var result = manager.GetConfig(typeof(IMySectionSettings));
            Assert.NotNull(result);
            Assert.True(result.Value.TryGetProperty("Enabled", out var enabled));
            Assert.Equal(JsonValueKind.True, enabled.ValueKind);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    public interface IMySectionSettings { }
    
}