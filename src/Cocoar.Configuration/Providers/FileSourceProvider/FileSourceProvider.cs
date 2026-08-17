namespace Cocoar.Configuration.Providers;

/// <summary>
/// Reads configuration from a JSON file. The file's bytes are already the JSON the pipeline merges, so this
/// provider adds no parsing on top of <see cref="FileBackedProvider"/> (which handles watching, path/symlink
/// security, debounce, and disposal).
/// </summary>
public sealed class FileSourceProvider(FileSourceProviderOptions options) : FileBackedProvider(options)
{
    protected override byte[] ParseToJsonBytes(byte[] rawFileBytes, string filename) => rawFileBytes;
}
