using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Infrastructure;

internal static partial class RecomputeCoalescerLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Error, Message = "Recompute failed from initial debounce trigger")]
    public static partial void InitialDebounceFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "Recompute failed from trailing trigger")]
    public static partial void TrailingTriggerFailed(this ILogger logger, Exception exception);
}

/// <summary>
/// Coalesces many incoming change signals into minimal recompute invocations while
/// preserving earliest-index semantics and providing an initial debounce plus trailing pass.
/// </summary>
internal sealed class RecomputeCoalescer(ILogger logger, Action<int> invoke, int initialDebounceMs, int trailingMs)
    : IDisposable
{
    private readonly int _initialDebounceMs = Math.Max(0, initialDebounceMs);
    private readonly int _trailingMs = Math.Max(0, trailingMs);

    private int _earliestPending = int.MaxValue;
    private int _earliestDuringRun = int.MaxValue;
    // 0 = false, 1 = true. Int is used (not bool) to support Interlocked.Exchange/CompareExchange atomics.
    private int _running;

    private System.Timers.Timer? _trailingTimer;
    private System.Timers.Timer? _initialTimer; // One-shot timer for initial debounce.
    private readonly Lock _lock = new();

    public void Signal(int index)
    {
        if (Volatile.Read(ref _running) == 1)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _earliestDuringRun);
                if (index >= current)
                {
                    return; 
                }
            } while (Interlocked.CompareExchange(ref _earliestDuringRun, index, current) != current);

            return;
        }

        int pending;
        do
        {
            pending = Volatile.Read(ref _earliestPending);
            if (pending == int.MaxValue)
            {
                if (Interlocked.CompareExchange(ref _earliestPending, index, int.MaxValue) == int.MaxValue)
                {
                    ScheduleInitialOrImmediate();
                    return;
                }
            }
            else
            {
                if (index >= pending)
                {
                    break;
                }
            }
        } while (Interlocked.CompareExchange(ref _earliestPending, index, pending) != pending);

        ScheduleTrailing();
    }

    private void ScheduleInitialOrImmediate()
    {
        if (_initialDebounceMs <= 0)
        {
            var idx = Interlocked.Exchange(ref _earliestPending, int.MaxValue);
            if (idx != int.MaxValue)
            {
                StartPass(idx);
            }

            return;
        }

        lock (_lock)
        {
            _initialTimer?.Dispose();
            _initialTimer = new(_initialDebounceMs) { AutoReset = false };
            _initialTimer.Elapsed += (_, _) =>
            {
                try
                {
                    // Double-check that we're still idle before starting pass
                    // Another thread might have started a pass in the meantime
                    if (Volatile.Read(ref _running) == 0)
                    {
                        var startIdx = Interlocked.Exchange(ref _earliestPending, int.MaxValue);
                        if (startIdx != int.MaxValue)
                        {
                            StartPass(startIdx);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.InitialDebounceFailed(ex);
                }
                finally
                {
                    lock (_lock)
                    {
                        _initialTimer?.Dispose();
                        _initialTimer = null;
                    }
                }
            };
            _initialTimer.Start();
        }
    }

    private void ScheduleTrailing()
    {
        if (_trailingMs <= 0)
        {
            var idx = Interlocked.Exchange(ref _earliestPending, int.MaxValue);
            if (idx != int.MaxValue)
            {
                StartPass(idx);
            }

            return;
        }

        lock (_lock)
        {
            if (_trailingTimer == null)
            {
                _trailingTimer = new(_trailingMs) { AutoReset = false };
                _trailingTimer.Elapsed += (_, _) =>
                {
                    try
                    {
                        // Double-check that we're still idle before starting pass
                        // Another thread might have started a pass in the meantime
                        if (Volatile.Read(ref _running) == 0)
                        {
                            var idx = Interlocked.Exchange(ref _earliestPending, int.MaxValue);
                            if (idx != int.MaxValue)
                            {
                                StartPass(idx);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.TrailingTriggerFailed(ex);
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            _trailingTimer?.Dispose();
                            _trailingTimer = null;
                        }
                    }
                };
            }
            else
            {
                _trailingTimer.Stop();
            }

            _trailingTimer.Start();
        }
    }

    private void StartPass(int idx)
    {
        // Critical: Set running state before clearing earliestDuringRun to prevent race condition
        // where Signal() might miss events during the state transition
        Interlocked.Exchange(ref _running, 1);
        Interlocked.Exchange(ref _earliestDuringRun, int.MaxValue);
        
        try
        {
            invoke(idx);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            var during = Interlocked.Exchange(ref _earliestDuringRun, int.MaxValue);
            if (during != int.MaxValue)
            {
                int current;
                do
                {
                    current = Volatile.Read(ref _earliestPending);
                    if (during >= current)
                    {
                        break;
                    }
                } while (Interlocked.CompareExchange(ref _earliestPending, during, current) != current);

                ScheduleTrailing();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _initialTimer?.Dispose();
            _initialTimer = null;
            _trailingTimer?.Dispose();
            _trailingTimer = null;
        }
    }
}
