namespace Cocoar.Configuration.Providers;

/// <summary>
/// File-based storage backend using atomic write-temp-then-rename pattern.
/// Default directory: {AppContext.BaseDirectory}/.cocoar/localStorage/
/// </summary>
public sealed class FileStorageBackend : IStorageBackend
{
    private readonly string _directory;

    public FileStorageBackend(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(AppContext.BaseDirectory, ".cocoar", "localStorage");
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct = default)
    {
        var path = GetFilePath(key);
        if (!File.Exists(path))
            return null;

        return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
    }

    public async Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);

        var path = GetFilePath(key);
        var tempPath = path + ".tmp";

        await File.WriteAllBytesAsync(tempPath, data, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    private string GetFilePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_directory, safeKey + ".json");
    }
}
