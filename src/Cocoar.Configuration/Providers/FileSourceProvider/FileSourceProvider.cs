using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class FileSourceProvider : ConfigurationProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>, IDisposable
{
    private readonly ConcurrentDictionary<string, JsonElement> _fileCache = new();
    private readonly ConcurrentDictionary<string, IObservable<JsonElement>> _changeStreams = new();
    
    private readonly Subject<FileSystemChange> _changeSubject = new();
    private readonly Timer _pollingTimer;
    private FileSystemWatcher? _fileSystemWatcher;
    private bool _isPolling;
    private readonly Lock _lockObj = new();
    private bool _disposed;

    public FileSourceProvider(FileSourceProviderOptions options) : base(options)
    {
        _pollingTimer = new(PollingCallback, null, Timeout.Infinite, Timeout.Infinite);
        
        StartMonitoring();
    }

    public override Task<JsonElement> FetchConfigurationAsync(FileSourceProviderQueryOptions queryOptions,
        CancellationToken ct = default)
    {
        var filename = queryOptions.Filename;
        if (!_fileCache.TryGetValue(filename, out var value))
        {
            value = LoadFile(filename);
            _fileCache[filename] = value;
        }

        return Task.FromResult(value);
    }

    public override IObservable<JsonElement> Changes(FileSourceProviderQueryOptions queryOptions)
    {
        var filename = queryOptions.Filename;
        return _changeStreams.GetOrAdd(filename, fn =>
            _changeSubject.AsObservable()
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath)
                                 .Equals(fn, StringComparison.OrdinalIgnoreCase)))
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
                    return newValue;
                })
                .Publish()
                .RefCount()
        );
    }

    private void StartMonitoring()
    {
        lock (_lockObj)
        {
            if (_disposed)
            {
                return;
            }

            if (Directory.Exists(ProviderOptions.Directory))
            {
                StartFileSystemWatcher();
            }
            else
            {
                StartPolling();
            }
        }
    }

    private void StartFileSystemWatcher()
    {
        lock (_lockObj)
        {
            if (_disposed || _fileSystemWatcher != null)
            {
                return;
            }

            try
            {
                _fileSystemWatcher = new(ProviderOptions.Directory, "*")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _fileSystemWatcher.Created += OnFileSystemEvent;
                _fileSystemWatcher.Changed += OnFileSystemEvent;
                _fileSystemWatcher.Deleted += OnFileSystemEvent;
                _fileSystemWatcher.Renamed += OnFileSystemRenamed;

                _fileSystemWatcher.Error += OnFileSystemWatcherError;

                _isPolling = false;
                _pollingTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch (Exception)
            {
                _fileSystemWatcher?.Dispose();
                _fileSystemWatcher = null;
                StartPolling();
            }
        }
    }

    private void StartPolling()
    {
        lock (_lockObj)
        {
            if (_disposed)
            {
                return;
            }

            StopFileSystemWatcher();
            _isPolling = true;
            
            _pollingTimer.Change(TimeSpan.Zero, ProviderOptions.PollingInterval);
        }
    }

    private void PollingCallback(object? state)
    {
        lock (_lockObj)
        {
            if (_disposed || !_isPolling)
            {
                return;
            }

            if (Directory.Exists(ProviderOptions.Directory))
            {
                // Directory now exists! Switch to FileSystemWatcher
                StartFileSystemWatcher();
            }
        }
    }

    private void OnFileSystemWatcherError(object sender, ErrorEventArgs e)
    {
        lock (_lockObj)
        {
            if (_disposed)
            {
                return;
            }

            // Log the error for diagnostics
            // Console.WriteLine($"FileSystemWatcher error: {e.GetException()}");
            
            StartPolling();
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        _changeSubject.OnNext(new(
            e.ChangeType switch
            {
                WatcherChangeTypes.Created => FileSystemChangeType.Created,
                WatcherChangeTypes.Changed => FileSystemChangeType.Changed,
                WatcherChangeTypes.Deleted => FileSystemChangeType.Deleted,
                _ => FileSystemChangeType.Changed
            },
            e.FullPath
        ));
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        _changeSubject.OnNext(new(
            FileSystemChangeType.Renamed,
            e.FullPath,
            e.OldFullPath
        ));
    }

    private void StopFileSystemWatcher()
    {
        _fileSystemWatcher?.Dispose();
        _fileSystemWatcher = null;
    }


    private JsonElement LoadFile(string filename)
    {
        var fullPath = Path.Combine(ProviderOptions.Directory, filename);
        
        // Throw specific exceptions so ConfigManager can handle Required vs Optional rules appropriately
        if (!Directory.Exists(ProviderOptions.Directory))
        {
            throw new DirectoryNotFoundException(
                $"Config directory doesn't exist: {ProviderOptions.Directory}. " +
                $"Check your path or mark the rule as Optional if this directory might not exist yet.");
        }
        
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Config file not found: {fullPath}. " +
                $"If this file is created later at runtime, mark the rule as Optional.", fullPath);
        }

        string json;
        // FileShare.ReadWrite allows other processes to write while we're reading - 
        // important for hot reload scenarios where a deployment might update the file
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            json = reader.ReadToEnd();
        }
        
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public void Dispose()
    {
        lock (_lockObj)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            StopFileSystemWatcher();
            _pollingTimer?.Dispose();
            _changeSubject?.Dispose();
        }
    }
}
