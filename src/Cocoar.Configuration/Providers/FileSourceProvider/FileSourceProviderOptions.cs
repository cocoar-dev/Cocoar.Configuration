using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public class FileSourceProviderOptions(string directory, TimeSpan? pollingInterval = null)
    : IProviderConfiguration
{
    public string Directory { get; } =
        Path.IsPathRooted(directory) ? directory : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, directory));

    public TimeSpan PollingInterval { get; } = pollingInterval ?? TimeSpan.FromSeconds(10);

    public string GenerateProviderKey()
        => $"{Directory}|{PollingInterval.TotalMilliseconds}";
}
