using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.FileSystem;

namespace Cocoar.Configuration.Providers;

public sealed class FileSourceProvider : ConfigurationProvider<FileSourceProviderOptions, FileSourceProviderQueryOptions>, IDisposable
{
    private readonly ConcurrentDictionary<string, IObservable<byte[]>> _changeBytesStreams = new();
    
    private readonly Subject<FileSystemChange> _changeSubject = new();
    private readonly ResilientFileSystemMonitor _monitor;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public FileSourceProvider(FileSourceProviderOptions options) : base(options)
    {
        _monitor = ResilientFileSystemMonitor
            .Watch(options.Directory, "*")
            .WithPollingFallback(options.PollingInterval)
            .Build();
        
        // Fire-and-forget background task for event processing - errors logged internally
        _ = Task.Run(async () => await ProcessFileSystemEventsAsync(_cts.Token).ConfigureAwait(false));
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
            _changeSubject.AsObservable()
                .Where(ev => Path.GetFileName(ev.Path).Equals(fn, StringComparison.OrdinalIgnoreCase) ||
                             (ev.OldPath != null && Path.GetFileName(ev.OldPath)
                                 .Equals(fn, StringComparison.OrdinalIgnoreCase)))
                // apply per-query debounce if provided; default is no debounce
                .Let(stream => queryOptions.DebounceTime is { } d && d > TimeSpan.Zero ? stream.Throttle(d) : stream)
                .Select(_ =>
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
                })
                .Publish()
                .RefCount()
        );
    }

    private byte[] LoadFileBytes(string filename)
    {
        var fullPath = Path.GetFullPath(Path.Combine(ProviderOptions.Directory, filename));
        
        // Prevent path traversal attacks - ensure resolved path is within configured directory
        var baseDir = Path.GetFullPath(ProviderOptions.Directory);
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
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
}
