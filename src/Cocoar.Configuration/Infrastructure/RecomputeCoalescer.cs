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
internal sealed class RecomputeCoalescer : IDisposable
{
    private readonly ILogger _logger;
    private readonly Action<int> _invoke;
    private readonly int _initialDebounceMs;
    private readonly int _trailingMs;

    private int _earliestPending = int.MaxValue;
    private int _earliestDuringRun = int.MaxValue;
    // 0 = false, 1 = true. Int is used (not bool) to support Interlocked.Exchange/CompareExchange atomics.
    private int _running;

    // Timers are created once and reused (stopped/started) to avoid GC pressure
    // under high-frequency file changes (P-03).
    private readonly System.Timers.Timer? _initialTimer;
    private readonly System.Timers.Timer? _trailingTimer;
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    public RecomputeCoalescer(ILogger logger, Action<int> invoke, int initialDebounceMs, int trailingMs)
    {
        _logger = logger;
        _invoke = invoke;
        _initialDebounceMs = Math.Max(0, initialDebounceMs);
        _trailingMs = Math.Max(0, trailingMs);

        if (_initialDebounceMs > 0)
        {
            _initialTimer = new(_initialDebounceMs) { AutoReset = false };
            _initialTimer.Elapsed += (_, _) =>
            {
                try
                {
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
                    _logger.InitialDebounceFailed(ex);
                }
            };
        }

        if (_trailingMs > 0)
        {
            _trailingTimer = new(_trailingMs) { AutoReset = false };
            _trailingTimer.Elapsed += (_, _) =>
            {
                try
                {
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
                    _logger.TrailingTriggerFailed(ex);
                }
            };
        }
    }

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
        if (_initialTimer == null)
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
            _initialTimer.Stop();
            _initialTimer.Start();
        }
    }

    private void ScheduleTrailing()
    {
        if (_trailingTimer == null)
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
            _trailingTimer.Stop();
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
            _invoke(idx);
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
            _trailingTimer?.Dispose();
        }
    }
}
