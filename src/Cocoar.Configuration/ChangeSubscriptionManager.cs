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
        Action recomputeCallback)
    {
        DisposeAllSubscriptions();

        foreach (var rm in ruleManagers)
        {
            var subscription = rm.Changes
                .Subscribe(_ =>
                {
                    try
                    {
                        recomputeCallback();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Recompute failed from change trigger");
                    }
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
