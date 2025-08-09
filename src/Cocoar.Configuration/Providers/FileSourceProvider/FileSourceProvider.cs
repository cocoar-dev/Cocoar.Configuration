using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

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

    public override Task<JsonElement> GetValueAsync(FileSourceProviderQueryOptions queryOptions, CancellationToken ct = default)
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
        return Task.FromResult(WrapIfNeeded(result, queryOptions.MemberWrapper));
    }

    public override IObservable<JsonElement> Changes(FileSourceProviderQueryOptions queryOptions)
    {
        var filename = queryOptions.Filename;
        return _changeStreams.GetOrAdd(filename, fn =>
            _fsObservable
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath).Equals(fn, StringComparison.OrdinalIgnoreCase)))
                // apply per-query debounce if provided; default is no debounce
                .Let(stream => queryOptions.Debounce is { } d && d > TimeSpan.Zero ? stream.Throttle(d) : stream)
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
        var fullPath = Path.Combine(ProviderOptions.Directory, filename);
        if (!File.Exists(fullPath))
        {
            // Throw to allow ConfigManager to honor Required rules
            throw new FileNotFoundException($"Config file not found: {fullPath}", fullPath);
        }
        var json = File.ReadAllText(fullPath);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
    
    public static ConfigRule CreateRule<TConfigType, TImplementationType>(string filepath, string? memberPath = null, string? memberWrapper = null, TimeSpan? debounceTime = null, Func<bool>? useWhen = null, bool required = false)
    {
        var directory = Path.GetDirectoryName(filepath) ?? string.Empty;
        var filename = Path.GetFileName(filepath);
        var instanceDebounce = debounceTime; // explicit naming to avoid confusion with query debounce
        var options = new FileSourceProviderOptions(directory, instanceDebounce);
        var queryDebounce = debounceTime;
        var queryOptions = new FileSourceProviderQueryOptions(filename, memberPath, memberWrapper, queryDebounce);
        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            options, 
            queryOptions, 
            new ConfigTypeDefinition(typeof(TConfigType), typeof(TImplementationType)),
            useWhen: useWhen,
            required: required
            );
    }
    
    public static ConfigRule CreateRule<TConfigType>(string filepath, string? memberPath = null, string? memberWrapper = null, TimeSpan? debounceTime = null, bool required = false)
    {
        var directory = Path.GetDirectoryName(filepath) ?? string.Empty;
        var filename = Path.GetFileName(filepath);
        var instanceDebounce = debounceTime;
        var options = new FileSourceProviderOptions(directory, instanceDebounce);
        var queryDebounce = debounceTime;
        var queryOptions = new FileSourceProviderQueryOptions(filename, memberPath, memberWrapper, queryDebounce);
        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(options, queryOptions, new ConfigTypeDefinition(typeof(TConfigType)), required: required);
    }

    public static ConfigRule CreateRule<TConfigType>(
        Func<ConfigManager, string> filepath,
        Func<ConfigManager, string?>? memberPath = null,
        Func<ConfigManager, string?>? memberWrapper = null,
        Func<ConfigManager, TimeSpan?>? debounceTime = null,
        Func<bool>? useWhen = null,
        bool required = true)
    {
        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            cm =>
            {
                var fp = filepath(cm);
                var dir = Path.GetDirectoryName(fp) ?? string.Empty;
                var instanceDebounce = debounceTime?.Invoke(cm);
                return new FileSourceProviderOptions(dir, instanceDebounce);
            },
            cm =>
            {
                var fp = filepath(cm);
                var file = Path.GetFileName(fp);
                var mp = memberPath?.Invoke(cm);
                var mw = memberWrapper?.Invoke(cm);
                var queryDebounce = debounceTime?.Invoke(cm);
                return new FileSourceProviderQueryOptions(file, mp, mw, queryDebounce);
            },
            new ConfigTypeDefinition(typeof(TConfigType)),
            useWhen,
            required
        );
    }

    public static ConfigRule CreateRule<TConfigType, TImplementationType>(
        Func<ConfigManager, string> filepath,
        Func<ConfigManager, string?>? memberPath = null,
        Func<ConfigManager, string?>? memberWrapper = null,
        Func<ConfigManager, TimeSpan?>? debounceTime = null,
        Func<bool>? useWhen = null,
        bool required = true)
    {
        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            cm =>
            {
                var fp = filepath(cm);
                var dir = Path.GetDirectoryName(fp) ?? string.Empty;
                var instanceDebounce = debounceTime?.Invoke(cm);
                return new FileSourceProviderOptions(dir, instanceDebounce);
            },
            cm =>
            {
                var fp = filepath(cm);
                var file = Path.GetFileName(fp);
                var mp = memberPath?.Invoke(cm);
                var mw = memberWrapper?.Invoke(cm);
                var queryDebounce = debounceTime?.Invoke(cm);
                return new FileSourceProviderQueryOptions(file, mp, mw, queryDebounce);
            },
            new ConfigTypeDefinition(typeof(TConfigType), typeof(TImplementationType)),
            useWhen,
            required
        );
    }
}
