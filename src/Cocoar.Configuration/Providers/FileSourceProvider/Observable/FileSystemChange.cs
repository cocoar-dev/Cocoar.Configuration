namespace Cocoar.Configuration.Providers;

public sealed record FileSystemChange(
    FileSystemChangeType ChangeType,
    string Path,
    string? OldPath = null);
