namespace Cocoar.Configuration.Providers;

// Combined options used by the fluent API to capture both instance and query options in one place.
public sealed class FileSourceRuleOptions
{
    public string Directory { get; }
    public string Filename { get; }
    public TimeSpan? DebounceTime { get; }
    public TimeSpan? PollingInterval { get; }
    
    public FileSourceRuleOptions(string directory, string filename, TimeSpan? debounceTime = null, TimeSpan? pollingInterval = null)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("directory is required", nameof(directory));
        if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("filename is required", nameof(filename));
        Directory = directory;
        Filename = filename;
        DebounceTime = debounceTime;
        PollingInterval = pollingInterval;
    }

    public static FileSourceRuleOptions FromFilePath(string filePath, TimeSpan? debounceTime = null, TimeSpan? pollingInterval = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is required", nameof(filePath));
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }
        var filename = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("filePath must include a filename", nameof(filePath));
        return new FileSourceRuleOptions(directory, filename, debounceTime, pollingInterval);
    }

    // Helpers to convert to existing provider/query options
    public FileSourceProviderOptions ToProviderOptions() => new(Directory, DebounceTime, PollingInterval);
    public FileSourceProviderQueryOptions ToQueryOptions() => new(Filename);
}
