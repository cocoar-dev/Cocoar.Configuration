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

        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            // Nothing persisted yet, or the file was removed between checks (TOCTOU-safe).
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);

        var path = GetFilePath(key);
        // Per-write unique temp name so concurrent writers never clobber each other's intermediate file.
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(tempPath, data, ct).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best-effort cleanup of a stale temp file; never mask the original write failure.
        }
    }

    private string GetFilePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_directory, safeKey + ".json");
    }
}
