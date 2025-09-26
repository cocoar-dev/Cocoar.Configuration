namespace Cocoar.Configuration.Providers.Tests;

/// <summary>
/// Utility for creating temporary directories with automatic cleanup.
/// Ensures deterministic directory operations for stress tests.
/// </summary>
public sealed class TempDirectoryHelper : IDisposable
{
    public string Path { get; }

    private TempDirectoryHelper(string path)
    {
        Path = path;
    }

    /// <summary>
    /// Create a temporary directory in the system temp location
    /// </summary>
    public static TempDirectoryHelper Create()
    {
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), 
            "cocoar_test_dir_" + Guid.NewGuid().ToString("N"));
        
        Directory.CreateDirectory(tempPath);
        return new(tempPath);
    }

    /// <summary>
    /// Create a temporary subdirectory in the specified parent
    /// </summary>
    public TempDirectoryHelper CreateSubdirectory(string name)
    {
        var subPath = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(subPath);
        return new(subPath);
    }

    /// <summary>
    /// Delete the directory and all contents
    /// </summary>
    public void Delete()
    {
        if (Directory.Exists(Path))
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup - sometimes files are still locked
            }
        }
    }

    public void Dispose()
    {
        Delete();
    }
}
