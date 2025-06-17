namespace Cocoar.Configuration.Extensions.Providers.FileSourceProvider;

public sealed record FileSystemChange(
    FileSystemChangeType ChangeType,
    string Path,
    string? OldPath = null);