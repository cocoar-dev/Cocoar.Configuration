using System.Reactive.Linq;
using System.Text.Json;

namespace Cocoar.Configuration.Extensions;

public sealed class FileConfigSourceProvider : ConfigSourceProvider<FileConfigSourceProviderOptions>
{
    private string _currentPath;
    private JsonElement? _cachedJson;
    private readonly TimeSpan _debounce;

    private readonly IObservable<ConfigChangeNotification> _changes;

    public override string SourceKind => "File";
    public override string SourceIdentifier => _currentPath;

    public FileConfigSourceProvider(FileConfigSourceProviderOptions options): base(options)
    {
        var filePath = options.Path;
        if (!Path.IsPathRooted(filePath))
            filePath = Path.GetFullPath(filePath);

        _currentPath = filePath;
        _debounce = options.DebounceTime ?? TimeSpan.FromMilliseconds(300);

        _changes = Observable.Defer(BuildWatcherStream)
            .Publish()
            .RefCount();
    }
    
    public override async Task<JsonElement?> GetValueAsync(string? part = null, CancellationToken ct = default)
    {
        if (_cachedJson is null) // lazy first-time load
            _cachedJson = LoadCurrent();

        if (_cachedJson is null) // file missing or invalid
            return null;

        if (string.IsNullOrWhiteSpace(part))
            return _cachedJson;

        return _cachedJson.Value.ValueKind == JsonValueKind.Object &&
               _cachedJson.Value.TryGetProperty(part, out var section)
            ? section
            : null;
    }

    public override IObservable<ConfigChangeNotification> Changes(string? part = null) =>
        part is null
            ? _changes
            : _changes.Select(c =>
            {
                var oldPart = SelectPart(c.OldValue, part);
                var newPart = SelectPart(c.NewValue, part);
                return new ConfigChangeNotification(part, newPart, oldPart);
            });

    // ── private helpers ─────────────────────────────────────────────────────
    private IObservable<ConfigChangeNotification> BuildWatcherStream()
    {
        var dir = Path.GetDirectoryName(_currentPath)!;

        var fs = new FileSystemObservable(
            dir,
            new FileSystemObservableOptions
            {
                Filter = "*", // catch rename
                DebounceTime = _debounce,
                IdentityMode = PathIdentityMode.CurrentOrOldPath
            });

        return fs
            .Where(ev =>
                ev.Path.Equals(_currentPath, StringComparison.OrdinalIgnoreCase) ||
                (ev.OldPath?.Equals(_currentPath, StringComparison.OrdinalIgnoreCase) ?? false))
            .SelectMany(async ev =>
            {
                if (ev.ChangeType == FileSystemChangeType.Renamed &&
                    ev.OldPath!.Equals(_currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    _currentPath = ev.Path; // follow rename
                }

                var oldJson = _cachedJson;
                _cachedJson = LoadCurrent(); // may become null
                return new ConfigChangeNotification(null, _cachedJson, oldJson);
            });
    }

    private JsonElement? LoadCurrent()
    {
        try
        {
            if (!File.Exists(_currentPath)) return null;
            var json = File.ReadAllText(_currentPath);
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch
        {
            return null; // malformed JSON or IO error
        }
    }

    private static JsonElement? SelectPart(JsonElement? root, string part) =>
        root is { ValueKind: JsonValueKind.Object } obj &&
        obj.TryGetProperty(part, out var section)
            ? section
            : null;
}

public record FileConfigSourceProviderOptions(string Path, TimeSpan? DebounceTime = null);
