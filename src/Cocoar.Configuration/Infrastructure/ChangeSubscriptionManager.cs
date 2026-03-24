using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Infrastructure;

internal static partial class ChangeSubscriptionManagerLog
{
    [LoggerMessage(EventId = 4100, Level = LogLevel.Error, Message = "Recompute failed from change trigger")]
    public static partial void RecomputeFailedFromChange(this ILogger logger, Exception exception);
}

internal class ChangeSubscriptionManager(ILogger logger) : IDisposable
{
    private readonly List<IDisposable> _changeSubscriptions = new();

    public void CreateSubscriptions(
        IEnumerable<IRuleManager> ruleManagers,
        Action<int> recomputeFromIndexCallback,
        int debounceMilliseconds = 300,
        int trailingMilliseconds = 40)
    {
        DisposeAllSubscriptions();
        var list = ruleManagers.ToList();
        var coalescer = new RecomputeCoalescer(logger, recomputeFromIndexCallback, debounceMilliseconds, trailingMilliseconds);
        _changeSubscriptions.Add(coalescer);

        for (var i = 0; i < list.Count; i++)
        {
            var idx = i;
            var rm = list[i];
            var subscription = rm.Changes.Subscribe(_ =>
            {
                try { coalescer.Signal(idx); }
                catch (Exception ex) { logger.RecomputeFailedFromChange(ex); }
            });
            _changeSubscriptions.Add(subscription);
        }
    }

    public void DisposeAllSubscriptions()
    {
        foreach (var subscription in _changeSubscriptions.ToArray())
        {
            Safety.DisposeQuietly(subscription);
        }

        _changeSubscriptions.Clear();
    }

    public void Dispose()
    {
        DisposeAllSubscriptions();
    }
}
