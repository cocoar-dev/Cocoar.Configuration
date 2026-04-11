using Xunit;

namespace Cocoar.Configuration.Providers.Tests.LocalStorage;

[Trait("Type", "Unit")]
[Trait("Provider", "LocalStorageProvider")]
public class FileStorageBackendTests : IDisposable
{
    private readonly string _testDir;

    public FileStorageBackendTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "cocoar_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task ReadAsync_MissingKey_ReturnsNull()
    {
        var backend = new FileStorageBackend(_testDir);
        var result = await backend.ReadAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_Roundtrip()
    {
        var backend = new FileStorageBackend(_testDir);
        var data = """{"Name":"Test","Value":42}"""u8.ToArray();

        await backend.WriteAsync("myKey", data);
        var result = await backend.ReadAsync("myKey");

        Assert.NotNull(result);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingData()
    {
        var backend = new FileStorageBackend(_testDir);
        var data1 = """{"Version":1}"""u8.ToArray();
        var data2 = """{"Version":2}"""u8.ToArray();

        await backend.WriteAsync("key", data1);
        await backend.WriteAsync("key", data2);
        var result = await backend.ReadAsync("key");

        Assert.Equal(data2, result);
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_testDir, "sub", "dir");
        var backend = new FileStorageBackend(nestedDir);
        var data = "{}"u8.ToArray();

        await backend.WriteAsync("key", data);

        Assert.True(Directory.Exists(nestedDir));
        var result = await backend.ReadAsync("key");
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task WriteAsync_SanitizesKey_PreventPathTraversal()
    {
        var backend = new FileStorageBackend(_testDir);
        var data = """{"safe":true}"""u8.ToArray();
        var dangerousKey = "..\\..\\etc\\passwd";

        await backend.WriteAsync(dangerousKey, data);

        // File should be inside _testDir, not escaped
        var files = Directory.GetFiles(_testDir, "*.json");
        Assert.Single(files);
        Assert.StartsWith(_testDir, files[0]);
    }

    [Fact]
    public async Task MultipleKeys_IndependentStorage()
    {
        var backend = new FileStorageBackend(_testDir);
        var data1 = """{"Type":"A"}"""u8.ToArray();
        var data2 = """{"Type":"B"}"""u8.ToArray();

        await backend.WriteAsync("keyA", data1);
        await backend.WriteAsync("keyB", data2);

        Assert.Equal(data1, await backend.ReadAsync("keyA"));
        Assert.Equal(data2, await backend.ReadAsync("keyB"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}
