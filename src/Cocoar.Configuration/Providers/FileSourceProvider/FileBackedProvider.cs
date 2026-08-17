using System.Collections.Concurrent;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.FileSystem;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Base class for file-backed providers. It owns everything format-agnostic — directory resolution,
/// resilient watching (with polling fallback), path-traversal and symlink protection, per-query debounce,
/// the change stream, and disposal — and delegates the single format-specific step to
/// <see cref="ParseToJsonBytes"/>. A concrete provider only converts the file's raw bytes to the UTF-8 JSON
/// the pipeline merges (the JSON provider returns them unchanged; YAML/dotenv/etc. parse and re-serialize).
/// </summary>
public abstract class FileBackedProvider
    : ConfigurationProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>, IDisposable
{
    private readonly ConcurrentDictionary<string, IObservable<byte[]>> _changeBytesStreams = new();

    private readonly SimpleSubject<FileSystemChange> _changeSubject = new();
    private readonly ResilientFileSystemMonitor _monitor;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    protected FileBackedProvider(FileSourceProviderOptions options) : base(options)
    {
        var monitorBuilder = ResilientFileSystemMonitor
            .Watch(options.Directory, "*")
            .WithPollingFallback(options.PollingInterval);

        if (options.FollowSymlinks)
        {
            // Kubernetes ConfigMap/Secret mounts update content by atomically swapping a sibling
            // "..data" symlink — the watched file's name and metadata are unchanged. Tracking the
            // resolved symlink target lets the monitor detect that swap and emit a change for the
            // user-visible file. (Capability lives in Cocoar.FileSystem 2.3.0+.)
            monitorBuilder = monitorBuilder.WithSymlinkTargetTracking();
        }

        _monitor = monitorBuilder.Build();

        // Background task for event processing — observe faults so they don't go unnoticed
        var monitorTask = Task.Run(async () => await ProcessFileSystemEventsAsync(_cts.Token).ConfigureAwait(false));
        var providerName = GetType().Name;
        monitorTask.ContinueWith(
            t => System.Diagnostics.Debug.Fail(
                $"{providerName}: file monitoring task faulted: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Converts a file's raw bytes to the UTF-8 JSON bytes the configuration pipeline merges. Called on the
    /// fetch path (where throwing signals a hard failure → Required rollback / Optional degrade) and on the
    /// change path (where a throw degrades to <c>{}</c>). For an already-JSON file this returns the bytes
    /// unchanged; for other formats, parse and serialize to JSON.
    /// </summary>
    /// <param name="rawFileBytes">The raw file contents (UTF-8 BOM already stripped).</param>
    /// <param name="filename">The file name, for diagnostics.</param>
    protected abstract byte[] ParseToJsonBytes(byte[] rawFileBytes, string filename);

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
        var json = ParseToJsonBytes(LoadRawFileBytes(filename), filename);
        return Task.FromResult(json);
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
                try
                {
                    return ParseToJsonBytes(LoadRawFileBytes(fn), fn);
                }
                catch
                {
                    return "{}"u8.ToArray();
                }
            });
        });
    }

    private byte[] LoadRawFileBytes(string filename)
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

        // Symlink / reparse-point handling. By default symlinks are rejected (defense in depth against
        // symlink-escape). When FollowSymlinks is enabled (e.g. Kubernetes ConfigMap/Secret mounts, where
        // every key is a symlink), the symlink is allowed only if its resolved final target stays within
        // the configured directory — preserving the escape protection.
        var fileInfo = new FileInfo(fullPath);
        if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            if (!ProviderOptions.FollowSymlinks)
            {
                throw new UnauthorizedAccessException(
                    $"Symlinks are not allowed for config files: {fullPath}. " +
                    $"Enable FollowSymlinks to read symlinked files (e.g. Kubernetes ConfigMap/Secret mounts).");
            }

            var finalTarget = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
            if (finalTarget is not null)
            {
                var resolvedFull = Path.GetFullPath(finalTarget.FullName);
                if (!resolvedFull.StartsWith(baseDirWithSep, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(resolvedFull, baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        $"Symlink target escapes the configured directory: '{filename}' resolves to " +
                        $"'{resolvedFull}', outside '{baseDir}'.");
                }
            }
            // The OS re-resolves the link on the read below; a swap between this check and the read is
            // only exploitable by something that can already write into the mount, which is out of scope.
        }

        // Use FileReader for secure file reading with shared access and BOM handling
        return FileReader.ReadAllBytes(fullPath, stripUtf8Bom: true);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return; // already disposed (race-safe)
        }

        try
        {
            _cts.Cancel();
        }
        catch (Exception)
        {
            // A cancellation callback faulting must not prevent the rest of Dispose from running.
        }

        _cts.Dispose();
        _monitor.Dispose();
        _changeSubject.Dispose();
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
