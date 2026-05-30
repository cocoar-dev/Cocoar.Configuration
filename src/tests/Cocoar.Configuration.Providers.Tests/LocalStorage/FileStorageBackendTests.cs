using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.LocalStorage;

[Trait("Type", "Unit")]
public class FileStorageBackendTests
{
    [Fact]
    public async Task ReadAsync_MissingKey_ReturnsNull()
    {
        using var dir = TempDirectoryHelper.Create();
        var backend = new FileStorageBackend(dir.Path);

        Assert.Null(await backend.ReadAsync("nope"));
    }

    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        using var dir = TempDirectoryHelper.Create();
        var backend = new FileStorageBackend(dir.Path);
        var payload = Encoding.UTF8.GetBytes("{\"a\":1}");

        await backend.WriteAsync("key", payload);
        var read = await backend.ReadAsync("key");

        Assert.NotNull(read);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(read!));
    }

    [Fact]
    public async Task Write_LeavesNoTempFiles()
    {
        using var dir = TempDirectoryHelper.Create();
        var backend = new FileStorageBackend(dir.Path);

        await backend.WriteAsync("key", Encoding.UTF8.GetBytes("{\"a\":1}"));

        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentWrites_NeverCorrupt()
    {
        using var dir = TempDirectoryHelper.Create();
        var backend = new FileStorageBackend(dir.Path);

        // Direct, unsynchronized concurrent writes to the same key. The per-write GUID temp guarantees no
        // writer clobbers another's intermediate file, so the destination is always a complete document.
        // (In production the store's write lock serializes writes; an overwrite move can still race
        // transiently on Windows, which we tolerate — we assert no corruption, not zero races.)
        var writes = Enumerable.Range(0, 50).Select(async i =>
        {
            try
            {
                await backend.WriteAsync("key", Encoding.UTF8.GetBytes($"{{\"v\":{i}}}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Transient overwrite-move race under pathological concurrency; not corruption.
            }
        });
        await Task.WhenAll(writes);

        var read = await backend.ReadAsync("key");
        Assert.NotNull(read);

        // Whatever ordering won, the persisted bytes must be a single, valid, uncorrupted JSON document...
        using var doc = JsonDocument.Parse(read!);
        Assert.True(doc.RootElement.TryGetProperty("v", out _));
        // ...and every per-write temp file must have been moved or cleaned up.
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentStoreWrites_AreSerializedAndConsistent()
    {
        using var dir = TempDirectoryHelper.Create();
        using var store = new LocalStorageStore(new FileStorageBackend(dir.Path), "key");

        // The real production path: writes go through the store's write lock and never throw or corrupt.
        var writes = Enumerable.Range(0, 50).Select(i =>
            store.WriteBytesAsync(Encoding.UTF8.GetBytes($"{{\"v\":{i}}}")));
        await Task.WhenAll(writes);

        var read = await store.ReadBytesAsync();
        using var doc = JsonDocument.Parse(read);
        Assert.True(doc.RootElement.TryGetProperty("v", out _));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }
}
