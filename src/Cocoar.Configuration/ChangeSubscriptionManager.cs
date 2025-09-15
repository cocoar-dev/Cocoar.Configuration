using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

/// <summary>
/// Manages reactive subscriptions to configuration changes and handles cleanup.
/// </summary>
internal class ChangeSubscriptionManager : IDisposable
{
    private readonly List<IDisposable> _changeSubscriptions = new();
    private readonly ILogger _logger;

    public ChangeSubscriptionManager(ILogger logger)
    {
        _logger = logger;
    }

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
        var coalescer = new RecomputeCoalescer(_logger, recomputeFromIndexCallback, debounceMilliseconds, trailingMilliseconds);
        _changeSubscriptions.Add(coalescer); // ensure disposal of timers

        for (int i = 0; i < list.Count; i++)
        {
            var idx = i;
            var rm = list[i];
            var subscription = rm.Changes.Subscribe(_ =>
            {
                try { coalescer.Signal(idx); }
                catch (Exception ex) { _logger.LogError(ex, "Recompute failed from change trigger"); }
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
            try
            {
                subscription.Dispose();
            }
            catch
            {
                /* ignore disposal errors */
            }
        }

        _changeSubscriptions.Clear();
    }

    public void Dispose()
    {
        DisposeAllSubscriptions();
        GC.SuppressFinalize(this);
    }
}
