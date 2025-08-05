using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text.Json;

namespace Cocoar.Configuration.Extensions.Providers.FileSourceProvider;

public sealed class FileSourceProvider(FileSourceProviderOptions options)
    : ConfigSourceProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>(options)
{
    private readonly ConcurrentDictionary<string, JsonElement?> _fileCache = new();
    private readonly ConcurrentDictionary<string, IObservable<ConfigChangeNotification>> _changeStreams = new();
    private readonly FileSystemObservable _fsObservable = new(
        options.Directory,
        new FileSystemObservableOptions
        {
            Filter = "*",
            DebounceTime = options.DebounceTime,
            IdentityMode = PathIdentityMode.CurrentOrOldPath
        });

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

        JsonElement? result = value;
        if (!string.IsNullOrWhiteSpace(queryOptions.MemberPath))
        {
            result = value.Value.ValueKind == JsonValueKind.Object &&
                     value.Value.TryGetProperty(queryOptions.MemberPath, out var section)
                ? section
                : null;
        }

        // Use the base class helper to wrap if needed
        return WrapIfNeeded(result, queryOptions.MemberWrapper);
    }

    public override IObservable<ConfigChangeNotification> Changes(FileSourceProviderQueryOptions queryOptions)
    {
        var filename = queryOptions.Filename;
        return _changeStreams.GetOrAdd(filename, fn =>
            _fsObservable
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath).Equals(fn, StringComparison.OrdinalIgnoreCase)))
                .Select(_ =>
                {
                    var oldValue = _fileCache.GetValueOrDefault(fn);
                    var newValue = LoadFile(fn);

                    JsonElement? oldSection = oldValue;
                    JsonElement? newSection = newValue;

                    if (queryOptions.MemberPath != null)
                    {
                        if (oldValue != null)
                        {
                            oldSection = oldValue.Value.ValueKind == JsonValueKind.Object &&
                                         oldValue.Value.TryGetProperty(queryOptions.MemberPath, out var section)
                                ? section
                                : null;
                        }

                        if (newValue != null)
                        {
                            newSection = newValue.Value.ValueKind == JsonValueKind.Object &&
                                         newValue.Value.TryGetProperty(queryOptions.MemberPath, out var section)
                                ? section
                                : null;
                        }
                    }

                    // Use the base class helper to wrap if needed
                    oldSection = WrapIfNeeded(oldSection, queryOptions.MemberWrapper);
                    newSection = WrapIfNeeded(newSection, queryOptions.MemberWrapper);

                    _fileCache[fn] = newValue;
                    return new ConfigChangeNotification(fn, newSection, oldSection);
                })
                .Publish()
                .RefCount()
        );
    }

    private JsonElement? LoadFile(string filename)
    {
        try
        {
            var fullPath = Path.Combine(ProviderOptions.Directory, filename);
            if (!File.Exists(fullPath)) return null;
            var json = File.ReadAllText(fullPath);
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
    
    public static ConfigRule CreateRule<TConfigType, TImplementationType>(string filepath, string? memberPath = null, string? memberWrapper = null, TimeSpan? debounceTime = null, Func<bool>? useWhen = null)
    {
        var directory = Path.GetDirectoryName(filepath);
        var filename = Path.GetFileName(filepath);
        var options = new FileSourceProviderOptions(directory, debounceTime);
        var queryOptions = new FileSourceProviderQueryOptions(filename, memberPath, memberWrapper);
        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            options, 
            queryOptions, 
            new ConfigTypeDefinition(typeof(TConfigType), typeof(TImplementationType)),
            useWhen: useWhen
            );
    }
    
    public static ConfigRule CreateRule<TConfigType>(string filepath, string? memberPath = null, string? memberWrapper = null, TimeSpan? debounceTime = null)
    {
        var directory = Path.GetDirectoryName(filepath);
        var filename = Path.GetFileName(filepath);
        var options = new FileSourceProviderOptions(directory, debounceTime);
        var queryOptions = new FileSourceProviderQueryOptions(filename, memberPath, memberWrapper);
        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(options, queryOptions, new ConfigTypeDefinition(typeof(TConfigType)));
    }
}
