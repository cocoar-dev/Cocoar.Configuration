using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Infrastructure;

/// <summary>
/// Manages reactive subscriptions to configuration changes and handles cleanup.
/// </summary>
internal class ChangeSubscriptionManager(ILogger logger) : IDisposable
{
    private readonly List<IDisposable> _changeSubscriptions = new();

    /// <summary>
    /// Creates subscriptions to rule manager changes that trigger recomputation.
    /// </summary>
    public void CreateSubscriptions(
        IEnumerable<RuleManager> ruleManagers,
        Action<int> recomputeFromIndexCallback,
        int debounceMilliseconds = 300,
        int trailingMilliseconds = 40)
    {
        DisposeAllSubscriptions();
        var list = ruleManagers.ToList();
        var coalescer = new RecomputeCoalescer(logger, recomputeFromIndexCallback, debounceMilliseconds, trailingMilliseconds);
        _changeSubscriptions.Add(coalescer); // ensure disposal of timers

        for (var i = 0; i < list.Count; i++)
        {
            var idx = i;
            var rm = list[i];
            var subscription = rm.Changes.Subscribe(_ =>
            {
                try { coalescer.Signal(idx); }
                catch (Exception ex) { logger.LogError(ex, "Recompute failed from change trigger"); }
            });
            _changeSubscriptions.Add(subscription);
        }
    }

    /// <summary>
    /// Disposes all current subscriptions.
    /// </summary>
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
        GC.SuppressFinalize(this);
    }
}
