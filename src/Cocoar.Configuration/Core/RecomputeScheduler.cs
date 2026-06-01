using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Manages scheduling and cancellation of configuration recompute operations.
/// Handles concurrency boundaries: cancels in-flight recomputes when new ones are triggered.
/// </summary>
internal sealed class RecomputeScheduler : IDisposable, IAsyncDisposable
{
#if NET9_0_OR_GREATER
    private readonly Lock _recomputeGate = new();
#else
    private readonly object _recomputeGate = new();
#endif
    private CancellationTokenSource? _recomputeCts;
    private Task? _currentRecomputeTask;
    private bool _disposed;

    /// <summary>
    /// Gets the currently running recompute task, if any.
    /// </summary>
    public Task? CurrentRecomputeTask => _currentRecomputeTask;

    /// <summary>
    /// Schedules a recompute operation. Cancels any in-flight recompute before starting a new one.
    /// </summary>
    /// <param name="recomputeAction">The action to execute for recomputation. Receives a CancellationToken.</param>
    public void Schedule(Action<CancellationToken> recomputeAction)
    {
        lock (_recomputeGate)
        {
            var cts = RenewCancellationSource();

            _currentRecomputeTask = Task.Run(() =>
            {
                recomputeAction(cts.Token);
            }, cts.Token);
        }
    }

    /// <summary>
    /// Schedules an async recompute operation. Cancels any in-flight recompute before starting a new one.
    /// </summary>
    /// <param name="recomputeAction">The async action to execute for recomputation. Receives a CancellationToken.</param>
    public void ScheduleAsync(Func<CancellationToken, Task> recomputeAction)
    {
        lock (_recomputeGate)
        {
            var cts = RenewCancellationSource();

            _currentRecomputeTask = Task.Run(async () =>
            {
                await recomputeAction(cts.Token).ConfigureAwait(false);
            }, cts.Token);
        }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCancellationSource();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCancellationSource();
        if (_currentRecomputeTask is { } task)
            await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }
}
