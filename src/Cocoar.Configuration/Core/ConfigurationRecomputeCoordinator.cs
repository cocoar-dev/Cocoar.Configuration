using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Core;

internal class ConfigurationRecomputeCoordinator(
    List<RuleManager> ruleManagers,
    ConfigurationOrchestrator orchestrator,
    ReactiveConfigManager reactiveConfigManager,
    ConfigurationHealthTracker healthTracker,
    ILogger logger)
    : IDisposable
{
    private readonly Lock _recomputeGate = new();
    private CancellationTokenSource? _recomputeCts;
    private Task? _currentRecomputeTask;

    public Task? CurrentRecomputeTask => _currentRecomputeTask;

    public void ScheduleRecompute(int startIndex, IConfigurationAccessor configAccessor)
    {
        lock (_recomputeGate)
        {
            var cts = RenewCancellationSource();

            _currentRecomputeTask = Task.Run(() =>
            {
                try
                {
                    orchestrator.RecomputeAllConfigurationsSafe(ruleManagers, configAccessor, startIndex, cts.Token);
                    healthTracker.ReportSuccessfulRecompute(startIndex);
                    reactiveConfigManager.NotifyConfigurationObservers(configAccessor.GetConfig);
                }
                catch (OperationCanceledException)
                {
                    // Swallow expected cancellation
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Runtime recompute failed - preserving current configuration");
                    healthTracker.ReportFailedRecompute(startIndex, ex);
                }
            }, cts.Token);
        }
    }

    public void Dispose()
    {
        DisposeCancellationSource();
    }

    private CancellationTokenSource RenewCancellationSource()
    {
        var newCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _recomputeCts, newCts);
        Safety.CancelAndDisposeQuietly(previous);
        return newCts;
    }

    private void DisposeCancellationSource()
    {
        var cts = Interlocked.Exchange(ref _recomputeCts, null);
        Safety.CancelAndDisposeQuietly(cts);
    }
}
