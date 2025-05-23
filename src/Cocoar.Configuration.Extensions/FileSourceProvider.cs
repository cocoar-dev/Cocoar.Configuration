using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.Json;

namespace Cocoar.Configuration.Extensions;

public sealed class FileConfigSourceProvider : ConfigSourceProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>
{
    private readonly FileSourceProviderOptions _providerOptions;
    private readonly ConcurrentDictionary<string, JsonElement?> _fileCache = new();
    private readonly ConcurrentDictionary<string, IObservable<ConfigChangeNotification>> _changeStreams = new();
    private readonly FileSystemObservable _fsObservable;

    public FileConfigSourceProvider(FileSourceProviderOptions options) : base(options)
    {
        _providerOptions = options;
        _fsObservable = new FileSystemObservable(
            options.Directory,
            new FileSystemObservableOptions
            {
                Filter = "*",
                DebounceTime = options.DebounceTime,
                IdentityMode = PathIdentityMode.CurrentOrOldPath
            });
    }

    public override async Task<JsonElement?> GetValueAsync(FileSourceProviderQueryOptions queryOptions, CancellationToken ct = default)
    {
        var filename = queryOptions.Filename;
        if (!_fileCache.TryGetValue(filename, out var value))
        {
            value = LoadFile(filename);
            _fileCache[filename] = value;
        }
        
        if (value == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(queryOptions.SectionName))
        {
            return value;
        }

        return value.Value.ValueKind == JsonValueKind.Object &&
               value.Value.TryGetProperty(queryOptions.SectionName, out var section)
            ? section
            : null;
    }

    public override IObservable<ConfigChangeNotification> Changes(FileSourceProviderQueryOptions queryOptions)
    {
        var filename = queryOptions.Filename;
        return _changeStreams.GetOrAdd(filename, fn =>
            _fsObservable
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath).Equals(fn, StringComparison.OrdinalIgnoreCase)))
                .Select(ev =>
                {
                    var oldValue = _fileCache.TryGetValue(fn, out var oldVal) ? oldVal : null;
                    var newValue = LoadFile(fn);

                    if (queryOptions.SectionName != null)
                    {
                        if (oldValue != null)
                        {
                            oldValue = oldValue.Value.ValueKind == JsonValueKind.Object &&
                                       oldValue.Value.TryGetProperty(queryOptions.SectionName, out var section)
                                ? section
                                : null;
                        }

                        if (newValue != null)
                        {
                            newValue = newValue.Value.ValueKind == JsonValueKind.Object &&
                                       newValue.Value.TryGetProperty(queryOptions.SectionName, out var section)
                                ? section
                                : null;
                        }
                    }

                    _fileCache[fn] = newValue;
                    return new ConfigChangeNotification(fn, newValue, oldValue);
                })
                .Publish()
                .RefCount()
        );
    }

    private JsonElement? LoadFile(string filename)
    {
        try
        {
            var fullPath = Path.Combine(_providerOptions.Directory, filename);
            if (!File.Exists(fullPath)) return null;
            var json = File.ReadAllText(fullPath);
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
}

public class FileSourceProviderOptions(string directory, TimeSpan? debounceTime = null)
    : ISourceProviderInstanceOptions
{
    private static readonly string BasePath = Path.GetDirectoryName((Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly()).Location)!;
    public string Directory { get; } = Path.IsPathRooted(directory) ? directory : Path.GetFullPath(Path.Combine(BasePath, directory));
    public TimeSpan? DebounceTime { get;} = debounceTime;
}


public record FileSourceProviderQueryOptions(string Filename, string? MemberPath = null, string? MemberWrapper = null): ISourceProviderQueryOptions;

public interface ISourceProviderQueryOptions
{
    string? MemberPath { get; }
    string? MemberWrapper { get; }
}