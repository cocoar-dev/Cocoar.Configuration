using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.FileSourceProvider;

public sealed class FileSourceProvider(FileSourceProviderOptions options)
    : ConfigurationProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>(options)
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

    public override Task<JsonElement> FetchConfigurationAsync(FileSourceProviderQueryOptions queryOptions, CancellationToken ct = default)
    {
        var filename = queryOptions.Filename;
        if (!_fileCache.TryGetValue(filename, out var value))
        {
            value = LoadFile(filename);
            _fileCache[filename] = value;
        }

        JsonElement result = value;
        if (!string.IsNullOrWhiteSpace(queryOptions.ConfigurationPath))
        {
            result = SelectByPath(value, queryOptions.ConfigurationPath);
        }

        return Task.FromResult(result);
    }

    public override IObservable<JsonElement> Changes(FileSourceProviderQueryOptions queryOptions)
    {
        var filename = queryOptions.Filename;
        return _changeStreams.GetOrAdd(filename, fn =>
            _fsObservable
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath).Equals(fn, StringComparison.OrdinalIgnoreCase)))
                // apply per-query debounce if provided; default is no debounce
                .Let(stream => queryOptions.DebounceTime is { } d && d > TimeSpan.Zero ? stream.Throttle(d) : stream)
                .Select(_ =>
                {
                    JsonElement newValue;
                    try
                    {
                        newValue = LoadFile(fn);
                    }
                    catch
                    {
                        // Avoid faulting the change stream; emit empty object instead
                        newValue = JsonDocument.Parse("{}").RootElement;
                    }
                    _fileCache[fn] = newValue;
                    JsonElement newSection = string.IsNullOrWhiteSpace(queryOptions.ConfigurationPath)
                        ? newValue
                        : SelectByPath(newValue, queryOptions.ConfigurationPath);
                    return newSection;
                })
                .Publish()
                .RefCount()
        );
    }

    private JsonElement LoadFile(string filename)
    {
        var fullPath = Path.Combine(ProviderOptions.Directory, filename);
        if (!File.Exists(fullPath))
        {
            // Throw to allow ConfigManager to honor Required rules
            throw new FileNotFoundException($"Config file not found: {fullPath}", fullPath);
        }
        var json = File.ReadAllText(fullPath);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
