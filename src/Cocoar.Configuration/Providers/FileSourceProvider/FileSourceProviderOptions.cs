using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public class FileSourceProviderOptions(string directory, TimeSpan? pollingInterval = null, bool followSymlinks = false)
    : IProviderConfiguration
{
    public string Directory { get; } =
        Path.IsPathRooted(directory) ? directory : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, directory));

    public TimeSpan PollingInterval { get; } = pollingInterval ?? TimeSpan.FromSeconds(10);

    /// <summary>
    /// When <c>true</c>, a symlinked config file is read (and its resolved target tracked for change
    /// detection) rather than rejected. Required for Kubernetes ConfigMap/Secret volume mounts, which
    /// expose each key as a symlink and update content by an atomic swap of a sibling <c>..data</c>
    /// symlink. The resolved final target must still resolve <i>within</i> <see cref="Directory"/>; a
    /// symlink whose target escapes the directory is rejected. Default <c>false</c> — symlinks are
    /// rejected as defense in depth against symlink-escape.
    /// </summary>
    public bool FollowSymlinks { get; } = followSymlinks;

    // FollowSymlinks is part of the key: a symlink-tracking monitor is configured differently from a
    // plain one, so two rules that differ only by this flag must not share a provider instance.
    public string GenerateProviderKey()
        => $"{Directory}|{PollingInterval.TotalMilliseconds}|{FollowSymlinks}";
}
