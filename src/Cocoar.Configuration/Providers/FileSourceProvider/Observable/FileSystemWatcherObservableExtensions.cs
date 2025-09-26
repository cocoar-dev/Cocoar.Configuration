using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace Cocoar.Configuration.Providers;

public static class FileSystemWatcherObservableExtensions
{
    public static IObservable<FileSystemChange[]> CollapseBurst(
        this IObservable<FileSystemChange> source,
        TimeSpan quietTime,
        Func<FileSystemChange, string> keySelector)
    {
        // Default to Scheduler.Default for production usage
        return CollapseBurst(source, quietTime, keySelector, Scheduler.Default);
    }

    public static IObservable<FileSystemChange[]> CollapseBurst(
        this IObservable<FileSystemChange> source,
        TimeSpan quietTime,
        Func<FileSystemChange, string> keySelector,
        IScheduler scheduler)
    {
        if (quietTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quietTime));
        }

        if (scheduler is null)
        {
            throw new ArgumentNullException(nameof(scheduler));
        }

        return source.Publish(shared =>
            shared
                .Window(() => shared.Throttle(quietTime, scheduler))
                .SelectMany(win =>
                    win.Aggregate(
                            new Dictionary<string, FileSystemChange>(),
                            (d, ch) =>
                            {
                                d[keySelector(ch)] = ch;
                                return d;
                            })
                        .Select(d => d.Values.ToArray())
                        .Where(arr => arr.Length > 0)));
    }
}
