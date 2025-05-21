
using System.Reactive.Linq;

namespace Cocoar.Configuration.Extensions;

public sealed class FileSystemObservable : IObservable<FileSystemChange>
{
    private readonly IObservable<FileSystemChange> _shared;

    public FileSystemObservable(
        string directory,
        FileSystemObservableOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory must be non-empty.", nameof(directory));

        options ??= new();

        // pick identity key once
        Func<FileSystemChange, string> key = options.IdentityMode switch
        {
            PathIdentityMode.CurrentOrOldPath => ch => ch.OldPath ?? ch.Path,
            _                                  => ch => ch.Path
        };

        // cold stream that owns the watcher
        IObservable<FileSystemChange> raw =
            Observable.Defer(() =>
            {
                var fsw = new FileSystemWatcher(directory, options.Filter)
                {
                    IncludeSubdirectories = options.IncludeSubdirectories,
                    NotifyFilter          = options.NotifyFilters,
                    EnableRaisingEvents   = true
                };

                IObservable<FileSystemChange> merged = Observable.Merge(
                    From(fsw,
                        (fs, h) => fs.Created += h,
                        (fs, h) => fs.Created -= h,
                        FileSystemChangeType.Created),

                    From(fsw,
                        (fs, h) => fs.Changed += h,
                        (fs, h) => fs.Changed -= h,
                        FileSystemChangeType.Changed),

                    From(fsw,
                        (fs, h) => fs.Deleted += h,
                        (fs, h) => fs.Deleted -= h,
                        FileSystemChangeType.Deleted),

                    Observable
                        .FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
                            h => fsw.Renamed += h,
                            h => fsw.Renamed -= h)
                        .Select(ep => ep.EventArgs)
                        .Select(a => new FileSystemChange(
                            FileSystemChangeType.Renamed,
                            a.FullPath,
                            a.OldFullPath))
                );

                return merged.Finally(fsw.Dispose);

                // helper that takes explicit add/remove delegates
                static IObservable<FileSystemChange> From(
                    FileSystemWatcher                 watcher,
                    Action<FileSystemWatcher,FileSystemEventHandler> add,
                    Action<FileSystemWatcher,FileSystemEventHandler> remove,
                    FileSystemChangeType              type)
                    => Observable
                        .FromEventPattern<FileSystemEventHandler,FileSystemEventArgs>(
                            h => add(watcher, h),
                            h => remove(watcher, h))
                        .Select(ep => ep.EventArgs)
                        .Select(a  => new FileSystemChange(type, a.FullPath));
            });

        // optional per-file debounce
        if (options.DebounceTime is { } win && win > TimeSpan.Zero)
        {
            raw = raw
                .GroupBy(key)
                .SelectMany(g => g.Throttle(win));
        }
            
        // share & ref-count
        _shared = raw.Publish().RefCount();
    }

    public IDisposable Subscribe(IObserver<FileSystemChange> observer)
        => _shared.Subscribe(observer);
}