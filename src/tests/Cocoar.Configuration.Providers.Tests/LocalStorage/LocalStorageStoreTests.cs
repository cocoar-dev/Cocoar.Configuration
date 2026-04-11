using Xunit;

namespace Cocoar.Configuration.Providers.Tests.LocalStorage;

[Trait("Type", "Unit")]
[Trait("Provider", "LocalStorageProvider")]
public class LocalStorageStoreTests : IDisposable
{
    private readonly string _testDir;

    public LocalStorageStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "cocoar_store_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    private LocalStorageStore CreateStore(string key = "TestConfig")
    {
        var backend = new FileStorageBackend(_testDir);
        return new LocalStorageStore(backend, key) { ConfigurationType = typeof(object) };
    }

    [Fact]
    public async Task ReadBytesAsync_NoData_ReturnsEmptyJson()
    {
        using var store = CreateStore();
        var result = await store.ReadBytesAsync();

        Assert.Equal("{}"u8.ToArray(), result);
    }

    [Fact]
    public async Task WriteBytesAsync_ThenRead_ReturnsWrittenData()
    {
        using var store = CreateStore();
        var data = """{"Name":"Written"}"""u8.ToArray();

        await store.WriteBytesAsync(data);
        var result = await store.ReadBytesAsync();

        Assert.Equal(data, result);
    }

    [Fact]
    public async Task WriteBytesAsync_SignalsChangeObservable()
    {
        using var store = CreateStore();
        var received = new List<byte[]>();
        using var sub = store.Changes.Subscribe(bytes => received.Add(bytes));

        var data = """{"Version":1}"""u8.ToArray();
        await store.WriteBytesAsync(data);

        Assert.Single(received);
        Assert.Equal(data, received[0]);
    }

    [Fact]
    public async Task WriteBytesAsync_MultipleWrites_AllSignaled()
    {
        using var store = CreateStore();
        var received = new List<byte[]>();
        using var sub = store.Changes.Subscribe(bytes => received.Add(bytes));

        await store.WriteBytesAsync("""{"V":1}"""u8.ToArray());
        await store.WriteBytesAsync("""{"V":2}"""u8.ToArray());
        await store.WriteBytesAsync("""{"V":3}"""u8.ToArray());

        Assert.Equal(3, received.Count);
    }

    [Fact]
    public async Task WriteBytesAsync_ConcurrentWrites_AreSerialized()
    {
        using var store = CreateStore();
        var received = new List<byte[]>();
        using var sub = store.Changes.Subscribe(bytes => received.Add(bytes));

        var tasks = Enumerable.Range(0, 10)
            .Select(i => store.WriteBytesAsync(System.Text.Encoding.UTF8.GetBytes($$"""{"I":{{i}}}""")))
            .ToArray();

        await Task.WhenAll(tasks);

        // All writes should complete and signal
        Assert.Equal(10, received.Count);

        // Final read should return one of the written values
        var final = await store.ReadBytesAsync();
        Assert.Contains("\"I\":", System.Text.Encoding.UTF8.GetString(final));
    }

    [Fact]
    public async Task WriteBytesAsync_BackendThrows_DoesNotSignalChange()
    {
        var failingBackend = new FailingStorageBackend();
        using var store = new LocalStorageStore(failingBackend, "key") { ConfigurationType = typeof(object) };
        var received = new List<byte[]>();
        using var sub = store.Changes.Subscribe(bytes => received.Add(bytes));

        await Assert.ThrowsAsync<IOException>(() =>
            store.WriteBytesAsync("""{"V":1}"""u8.ToArray()));

        Assert.Empty(received);
    }

    [Fact]
    public async Task ReplaceBackend_SwitchesStorageAtRuntime()
    {
        var dir1 = Path.Combine(_testDir, "backend1");
        var dir2 = Path.Combine(_testDir, "backend2");
        var backend1 = new FileStorageBackend(dir1);
        var backend2 = new FileStorageBackend(dir2);

        using var store = new LocalStorageStore(backend1, "key") { ConfigurationType = typeof(object) };

        // Write to backend1
        await store.WriteBytesAsync("""{"From":"backend1"}"""u8.ToArray());
        var result1 = await store.ReadBytesAsync();
        Assert.Contains("backend1", System.Text.Encoding.UTF8.GetString(result1));

        // Swap to backend2
        store.ReplaceBackend(backend2);

        // Read returns empty (backend2 has no data)
        var result2 = await store.ReadBytesAsync();
        Assert.Equal("{}"u8.ToArray(), result2);

        // Write goes to backend2
        await store.WriteBytesAsync("""{"From":"backend2"}"""u8.ToArray());
        var result3 = await store.ReadBytesAsync();
        Assert.Contains("backend2", System.Text.Encoding.UTF8.GetString(result3));

        // backend1 still has its old data
        var oldData = await backend1.ReadAsync("key");
        Assert.NotNull(oldData);
        Assert.Contains("backend1", System.Text.Encoding.UTF8.GetString(oldData));
    }

    [Fact]
    public void ConfigurationType_SetViaInit()
    {
        var backend = new FileStorageBackend(_testDir);
        var store = new LocalStorageStore(backend, "key") { ConfigurationType = typeof(string) };
        Assert.Equal(typeof(string), store.ConfigurationType);
        store.Dispose();
    }

    private sealed class FailingStorageBackend : IStorageBackend
    {
        public Task<byte[]?> ReadAsync(string key, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);

        public Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
            => throw new IOException("Disk full");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}
