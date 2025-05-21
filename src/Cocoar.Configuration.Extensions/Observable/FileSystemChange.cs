namespace Cocoar.Configuration.Extensions;

public sealed record FileSystemChange(
    FileSystemChangeType ChangeType,
    string               Path,
    string?              OldPath = null);