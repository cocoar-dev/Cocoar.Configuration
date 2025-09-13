using System.Reflection;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.FileSourceProvider;

public class FileSourceProviderOptions(string directory, TimeSpan? debounceTime = null)
    : IProviderConfiguration
{
    private static readonly string BasePath = Path.GetDirectoryName((Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly()).Location)!;
    public string Directory { get; } = Path.IsPathRooted(directory) ? directory : Path.GetFullPath(Path.Combine(BasePath, directory));
    public TimeSpan? DebounceTime { get;} = debounceTime;

    // Reuse provider by directory regardless of debounce differences between rules
    public string GenerateProviderKey()
        => Directory;
}
