using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text.Json;

namespace Cocoar.Configuration.Providers.FileSourceProvider;

public sealed class FileSourceProvider(FileSourceProviderOptions options)
    : ConfigSourceProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>(options)
{
    private readonly ConcurrentDictionary<string, JsonElement> _fileCache = new();
    private readonly ConcurrentDictionary<string, IObservable<JsonElement>> _changeStreams = new();
    private readonly FileSystemObservable _fsObservable = new(
        options.Directory,
        new FileSystemObservableOptions
        {
            Filter = "*",
            DebounceTime = options.DebounceTime,
            IdentityMode = PathIdentityMode.CurrentOrOldPath
        });

    public override async Task<JsonElement> GetValueAsync(FileSourceProviderQueryOptions queryOptions, CancellationToken ct = default)
    {
        var filename = queryOptions.Filename;
        if (!_fileCache.TryGetValue(filename, out var value))
        {
            value = LoadFile(filename);
            _fileCache[filename] = value;
        }

        JsonElement result = value;
        if (!string.IsNullOrWhiteSpace(queryOptions.MemberPath))
        {
            result = value.ValueKind == JsonValueKind.Object &&
                     value.TryGetProperty(queryOptions.MemberPath, out var section)
                ? section
                : JsonDocument.Parse("{}").RootElement;
        }

        // Use the base class helper to wrap if needed
        return WrapIfNeeded(result, queryOptions.MemberWrapper);
    }

    public override IObservable<JsonElement> Changes(FileSourceProviderQueryOptions queryOptions)
    {
        var filename = queryOptions.Filename;
        return _changeStreams.GetOrAdd(filename, fn =>
            _fsObservable
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath).Equals(fn, StringComparison.OrdinalIgnoreCase)))
                .Select(_ =>
                {
                    var newValue = LoadFile(fn);
                    _fileCache[fn] = newValue;
                    JsonElement newSection = newValue;
                    if (!string.IsNullOrWhiteSpace(queryOptions.MemberPath))
                    {
                        newSection = newValue.ValueKind == JsonValueKind.Object &&
                                     newValue.TryGetProperty(queryOptions.MemberPath, out var section)
                            ? section
                            : JsonDocument.Parse("{}").RootElement;
                    }
                    return WrapIfNeeded(newSection, queryOptions.MemberWrapper);
                })
                .Publish()
                .RefCount()
        );
    }

    private JsonElement LoadFile(string filename)
    {
        try
        {
            var fullPath = Path.Combine(ProviderOptions.Directory, filename);
            if (!File.Exists(fullPath))
            {
                return JsonDocument.Parse("{}").RootElement;
            }
            var json = File.ReadAllText(fullPath);
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch
        {
            return JsonDocument.Parse("{}").RootElement;
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
