namespace Cocoar.Configuration.Providers.FileSourceProvider;

public sealed record FileSystemChange(
    FileSystemChangeType ChangeType,
    string Path,
    string? OldPath = null);
