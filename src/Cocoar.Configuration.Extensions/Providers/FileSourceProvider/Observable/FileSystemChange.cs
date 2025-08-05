namespace Cocoar.Configuration.Extensions.Providers;

public sealed record FileSystemChange(
    FileSystemChangeType ChangeType,
    string Path,
    string? OldPath = null);