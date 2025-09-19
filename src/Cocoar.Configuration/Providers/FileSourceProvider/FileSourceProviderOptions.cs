using System.Reflection;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public class FileSourceProviderOptions(string directory, TimeSpan? debounceTime = null, TimeSpan? pollingInterval = null)
    : IProviderConfiguration
{
    private static readonly string BasePath =
        Path.GetDirectoryName((Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly()).Location)!;

    public string Directory { get; } =
        Path.IsPathRooted(directory) ? directory : Path.GetFullPath(Path.Combine(BasePath, directory));

    public TimeSpan? DebounceTime { get; } = debounceTime;

    /// <summary>
    /// Polling interval for directory existence checks when FileSystemWatcher can't be used.
    /// Default is 10 seconds. Use shorter intervals for testing scenarios.
    /// </summary>
    public TimeSpan PollingInterval { get; } = pollingInterval ?? TimeSpan.FromSeconds(10);

    // Reuse provider by directory AND polling interval - different intervals need separate providers
    public string GenerateProviderKey()
        => $"{Directory}|{PollingInterval.TotalMilliseconds}";
}
