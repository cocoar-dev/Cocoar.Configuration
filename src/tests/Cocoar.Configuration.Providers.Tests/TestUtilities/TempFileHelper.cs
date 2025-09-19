using System;
using System.IO;
using System.Text;

namespace Cocoar.Configuration.Providers.Tests;

/// <summary>
/// Utility for creating temporary files with automatic cleanup.
/// Ensures deterministic file operations and proper disposal.
/// </summary>
public sealed class TempFileHelper : IDisposable
{
    public string FilePath { get; }
    public string Directory { get; }

    private TempFileHelper(string directory, string fileName, string? initialContent)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, fileName);
        
        if (initialContent != null)
        {
            WriteContent(initialContent);
        }
    }

    /// <summary>
    /// Create temp file in system temp directory with random name
    /// </summary>
    public static TempFileHelper Create(string? initialContent = null, string extension = ".json")
    {
        var fileName = "cocoar_test_" + Guid.NewGuid().ToString("N") + extension;
        var tempDir = Path.GetTempPath();
        return new TempFileHelper(tempDir, fileName, initialContent);
    }

    /// <summary>
    /// Create temp file in specific directory
    /// </summary>
    public static TempFileHelper CreateInDirectory(string directory, string fileName, string? initialContent = null)
    {
        System.IO.Directory.CreateDirectory(directory);
        return new TempFileHelper(directory, fileName, initialContent);
    }

    /// <summary>
    /// Write content to the file with proper sharing for concurrent access
    /// </summary>
    public void WriteContent(string content)
    {
        // Use FileShare.ReadWrite to match FileSourceProvider behavior
        using var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    /// <summary>
    /// Write JSON object as string
    /// </summary>
    public void WriteJson(object obj)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        WriteContent(json);
    }

    /// <summary>
    /// Read current file content
    /// </summary>
    public string ReadContent()
    {
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Delete the file if it exists
    /// </summary>
    public void Delete()
    {
        if (System.IO.File.Exists(FilePath))
        {
            System.IO.File.Delete(FilePath);
        }
    }

    public void Dispose()
    {
        Delete();
    }
}