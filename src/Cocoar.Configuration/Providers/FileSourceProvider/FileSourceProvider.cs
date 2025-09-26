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
    
    // Resilient FileSystemWatcher components
    private readonly Subject<FileSystemChange> _changeSubject = new();
    private readonly Timer _pollingTimer;
    private FileSystemWatcher? _fileSystemWatcher;
    private bool _isPolling;
    private readonly object _lockObj = new();
    private bool _disposed;

    public FileSourceProvider(FileSourceProviderOptions options) : base(options)
    {
        // Initialize polling timer (initially disabled)
        _pollingTimer = new(PollingCallback, null, Timeout.Infinite, Timeout.Infinite);
        
        // Start appropriate monitoring mode
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

    /// <summary>
    /// Start appropriate monitoring mode: FileSystemWatcher if directory exists, polling if not.
    /// </summary>
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

    /// <summary>
    /// Start FileSystemWatcher for fast change detection.
    /// Monitors Error event for directory deletion/access issues.
    /// </summary>
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

                // File events
                _fileSystemWatcher.Created += OnFileSystemEvent;
                _fileSystemWatcher.Changed += OnFileSystemEvent;
                _fileSystemWatcher.Deleted += OnFileSystemEvent;
                _fileSystemWatcher.Renamed += OnFileSystemRenamed;

                // KEY: Monitor Error event for watcher failure
                _fileSystemWatcher.Error += OnFileSystemWatcherError;

                _isPolling = false;
                _pollingTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop polling
            }
            catch (Exception)
            {
                // If FileSystemWatcher creation fails, fall back to polling
                _fileSystemWatcher?.Dispose();
                _fileSystemWatcher = null;
                StartPolling();
            }
        }
    }

    /// <summary>
    /// Start Directory.Exists polling when FileSystemWatcher can't be used.
    /// Uses configurable polling interval for directory existence checks.
    /// </summary>
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
            
            // Start polling at configured interval (default 10 seconds)
            _pollingTimer.Change(TimeSpan.Zero, ProviderOptions.PollingInterval);
        }
    }

    /// <summary>
    /// Polling callback that checks if directory exists and can restart FileSystemWatcher.
    /// </summary>
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

    /// <summary>
    /// Handle FileSystemWatcher Error event - indicates watcher failure.
    /// Falls back to polling until directory becomes accessible again.
    /// </summary>
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
            
            // Watcher has failed, fall back to polling
            StartPolling();
        }
    }

    /// <summary>
    /// Handle standard FileSystemWatcher events.
    /// </summary>
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

    /// <summary>
    /// Handle FileSystemWatcher rename events.
    /// </summary>
    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        _changeSubject.OnNext(new(
            FileSystemChangeType.Renamed,
            e.FullPath,
            e.OldFullPath
        ));
    }

    /// <summary>
    /// Stop FileSystemWatcher and clean up resources.
    /// </summary>
    private void StopFileSystemWatcher()
    {
        _fileSystemWatcher?.Dispose();
        _fileSystemWatcher = null;
    }

    /// <summary>
    /// Load file with proper error handling for Required vs Optional rules.
    /// During polling mode, this is the primary way configuration is accessed.
    /// </summary>
    private JsonElement LoadFile(string filename)
    {
        var fullPath = Path.Combine(ProviderOptions.Directory, filename);
        
        // CRITICAL: Handle directory existence for polling mode
        if (!Directory.Exists(ProviderOptions.Directory))
        {
            // Directory doesn't exist - throw to allow ConfigManager to honor Required rules
            throw new DirectoryNotFoundException($"Config directory not found: {ProviderOptions.Directory}");
        }
        
        if (!File.Exists(fullPath))
        {
            // File doesn't exist - throw to allow ConfigManager to honor Required rules  
            throw new FileNotFoundException($"Config file not found: {fullPath}", fullPath);
        }

        // Use explicit FileShare.ReadWrite to avoid locking conflicts
        // This allows other processes (including rapid test writes) to access the file
        string json;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            json = reader.ReadToEnd();
        }
        
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// Dispose resources: FileSystemWatcher, polling timer, and subjects.
    /// </summary>
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
