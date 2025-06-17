using System.Reactive.Linq;

namespace Cocoar.Configuration.Extensions.Providers.FileSourceProvider;

public static class FileSystemWatcherObservableExtensions
{
    public static IObservable<FileSystemChange[]> CollapseBurst(
        this IObservable<FileSystemChange> source,
        TimeSpan quietTime,
        Func<FileSystemChange, string> keySelector)
    {
        if (quietTime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(quietTime));

        return source.Publish(shared =>
            shared
                .Window(() => shared.Throttle(quietTime))
                .SelectMany(win =>
                    win.Aggregate(
                            new Dictionary<string, FileSystemChange>(),
                            (d, ch) =>
                            {
                                d[keySelector(ch)] = ch;
                                return d;
                            })
                        .Select(d => d.Values.ToArray())));
    }
}