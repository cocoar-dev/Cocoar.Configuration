using System.Collections.Concurrent;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.FileSystem;

namespace Cocoar.Configuration.Providers;

public sealed class FileSourceProvider : ConfigurationProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>, IDisposable
{
    private readonly ConcurrentDictionary<string, IObservable<byte[]>> _changeBytesStreams = new();

    private readonly SimpleSubject<FileSystemChange> _changeSubject = new();
    private readonly ResilientFileSystemMonitor _monitor;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public FileSourceProvider(FileSourceProviderOptions options) : base(options)
    {
        _monitor = ResilientFileSystemMonitor
            .Watch(options.Directory, "*")
            .WithPollingFallback(options.PollingInterval)
            .Build();

        // Background task for event processing — observe faults so they don't go unnoticed
        var monitorTask = Task.Run(async () => await ProcessFileSystemEventsAsync(_cts.Token).ConfigureAwait(false));
        monitorTask.ContinueWith(
            static t => System.Diagnostics.Debug.Fail(
                $"FileSourceProvider: file monitoring task faulted: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private async Task ProcessFileSystemEventsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _monitor.Events.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var changeType = evt.Kind switch
                {
                    FileSystemEventKind.Created => FileSystemChangeType.Created,
                    FileSystemEventKind.Changed => FileSystemChangeType.Changed,
                    FileSystemEventKind.Deleted => FileSystemChangeType.Deleted,
                    FileSystemEventKind.Renamed => FileSystemChangeType.Renamed,
                    _ => (FileSystemChangeType?)null
                };

                if (changeType.HasValue)
                {
                    _changeSubject.OnNext(new(changeType.Value, evt.FullPath, evt.OldFullPath));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override Task<byte[]> FetchConfigurationBytesAsync(FileSourceProviderQueryOptions queryOptions,
        CancellationToken ct = default)
    {
        var filename = queryOptions.Filename;
        var bytes = LoadFileBytes(filename);
        return Task.FromResult(bytes);
    }

    public override IObservable<byte[]> ChangesAsBytes(FileSourceProviderQueryOptions queryOptions)
    {
        var filename = queryOptions.Filename;
        return _changeBytesStreams.GetOrAdd(filename, fn =>
        {
            IObservable<FileSystemChange> filtered = ((IObservable<FileSystemChange>)_changeSubject)
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath)
                                 .Equals(fn, StringComparison.OrdinalIgnoreCase)));

            // Apply per-query debounce if provided; default is no debounce
            if (queryOptions.DebounceTime is { } d && d > TimeSpan.Zero)
            {
                filtered = new ThrottleObservable<FileSystemChange>(filtered, d);
            }

            return filtered.Select(_ =>
            {
                byte[] newBytes;
                try
                {
                    newBytes = LoadFileBytes(fn);
                }
                catch
                {
                    newBytes = "{}"u8.ToArray();
                }
                return newBytes;
            });
        });
    }

    private byte[] LoadFileBytes(string filename)
    {
        var fullPath = Path.GetFullPath(Path.Combine(ProviderOptions.Directory, filename));

        // Prevent path traversal attacks - ensure resolved path is within configured directory.
        // Append trailing separator so "config_backup/../" can't escape a "config" base dir.
        var baseDir = Path.GetFullPath(ProviderOptions.Directory);
        var baseDirWithSep = baseDir.EndsWith(Path.DirectorySeparatorChar)
            ? baseDir
            : baseDir + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(baseDirWithSep, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, baseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path traversal detected: filename '{filename}' resolves outside configured directory. " +
                $"Resolved: {fullPath}, Expected base: {baseDir}");
        }

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

        // Reject symlinks / reparse points to prevent symlink escape attacks
        var fileInfo = new FileInfo(fullPath);
        if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException(
                $"Symlinks are not allowed for config files: {fullPath}");
        }

        // Use FileReader for secure file reading with shared access and BOM handling
        return FileReader.ReadAllBytes(fullPath, stripUtf8Bom: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _monitor?.Dispose();
        _changeSubject?.Dispose();
    }

    /// <summary>
    /// Trailing-edge throttle (debounce): emits the last received value after a quiet period.
    /// </summary>
    private sealed class ThrottleObservable<T>(IObservable<T> source, TimeSpan dueTime) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer)
        {
            var state = new ThrottleState(observer, dueTime);
            var sub = source.Subscribe(state);
            return DisposableHelpers.Create(() =>
            {
                sub.Dispose();
                state.Dispose();
            });
        }

        private sealed class ThrottleState(IObserver<T> target, TimeSpan dueTime) : IObserver<T>, IDisposable
        {
#if NET9_0_OR_GREATER
            private readonly Lock _lock = new();
#else
            private readonly object _lock = new();
#endif
            private Timer? _timer;
            private T? _latestValue;
            private bool _hasValue;
            private bool _disposed;

            public void OnNext(T value)
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _latestValue = value;
                    _hasValue = true;
                    if (_timer is null)
                        _timer = new Timer(Tick, null, dueTime, Timeout.InfiniteTimeSpan);
                    else
                        _timer.Change(dueTime, Timeout.InfiniteTimeSpan);
                }
            }

            public void OnError(Exception error) => target.OnError(error);
            public void OnCompleted() => target.OnCompleted();

            private void Tick(object? _)
            {
                T value;
                lock (_lock)
                {
                    if (!_hasValue || _disposed) return;
                    value = _latestValue!;
                    _hasValue = false;
                }

                target.OnNext(value);
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    _disposed = true;
                    _hasValue = false;
                    _timer?.Dispose();
                    _timer = null;
                }
            }
        }
    }
}
