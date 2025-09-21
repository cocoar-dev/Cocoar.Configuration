namespace Cocoar.Configuration.Providers;

public sealed class FileSystemObservableOptions
{
    public string Filter { get; init; } = "*";
    public bool IncludeSubdirectories { get; init; } = true;

    public NotifyFilters NotifyFilters { get; init; } =
        NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;

    public TimeSpan? DebounceTime { get; init; } = null;
    public TimeSpan? BurstQuietTime { get; init; } = null;

    public PathIdentityMode IdentityMode { get; init; } =
        PathIdentityMode.CurrentPathOnly;
}
