using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

/// <summary>
/// Coalesces many incoming change signals into minimal recompute invocations while
/// preserving earliest-index semantics and providing an initial debounce plus trailing pass.
/// </summary>
internal sealed class RecomputeCoalescer : IDisposable
{
    private readonly ILogger _logger;
    private readonly int _initialDebounceMs;
    private readonly int _trailingMs;
    private readonly Action<int> _invoke;

    private int _earliestPending = int.MaxValue; // Pending (not yet started) earliest index.
    private int _earliestDuringRun = int.MaxValue; // Events that arrive while a pass is running.
    private int _running = 0; // 0 = idle, 1 = running.

    private System.Timers.Timer? _trailingTimer;
    private System.Timers.Timer? _initialTimer; // One-shot timer for initial debounce.
    private readonly object _lock = new(); // Protect timer lifecycle.

    public RecomputeCoalescer(ILogger logger, Action<int> invoke, int initialDebounceMs, int trailingMs)
    {
        _logger = logger;
        _invoke = invoke;
        _initialDebounceMs = Math.Max(0, initialDebounceMs);
        _trailingMs = Math.Max(0, trailingMs);
    }

    public void Signal(int index)
    {
        if (Volatile.Read(ref _running) == 1)
        {
            // Coalesce into earliestDuringRun while a pass is executing.
            int current;
            do
            {
                current = Volatile.Read(ref _earliestDuringRun);
                if (index >= current) return; // existing earlier or equal index already captured
            } while (Interlocked.CompareExchange(ref _earliestDuringRun, index, current) != current);

            return;
        }

        // Idle path: fold into _earliestPending; if this is the first pending event, schedule initial debounce/start.
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
                if (index >= pending) break; // existing earlier or equal pending index
            }
        } while (Interlocked.CompareExchange(ref _earliestPending, index, pending) != pending);

        ScheduleTrailing();
    }

    private void ScheduleInitialOrImmediate()
    {
        if (_initialDebounceMs <= 0)
        {
            var idx = Interlocked.Exchange(ref _earliestPending, int.MaxValue);
            if (idx != int.MaxValue) StartPass(idx);
            return;
        }

        lock (_lock)
        {
            _initialTimer?.Dispose();
            _initialTimer = new System.Timers.Timer(_initialDebounceMs) { AutoReset = false };
            _initialTimer.Elapsed += (_, _) =>
            {
                try
                {
                    // Double-check that we're still idle before starting pass
                    // Another thread might have started a pass in the meantime
                    if (Volatile.Read(ref _running) == 0)
                    {
                        var startIdx = Interlocked.Exchange(ref _earliestPending, int.MaxValue);
                        if (startIdx != int.MaxValue) StartPass(startIdx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recompute failed from initial debounce trigger");
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
            if (idx != int.MaxValue) StartPass(idx);
            return;
        }

        lock (_lock)
        {
            if (_trailingTimer == null)
            {
                _trailingTimer = new System.Timers.Timer(_trailingMs) { AutoReset = false };
                _trailingTimer.Elapsed += (_, _) =>
                {
                    try
                    {
                        // Double-check that we're still idle before starting pass
                        // Another thread might have started a pass in the meantime
                        if (Volatile.Read(ref _running) == 0)
                        {
                            var idx = Interlocked.Exchange(ref _earliestPending, int.MaxValue);
                            if (idx != int.MaxValue) StartPass(idx);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Recompute failed from trailing trigger");
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
            _invoke(idx);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            var during = Interlocked.Exchange(ref _earliestDuringRun, int.MaxValue);
            if (during != int.MaxValue)
            {
                // Fold into pending earliest and schedule trailing immediately.
                int current;
                do
                {
                    current = Volatile.Read(ref _earliestPending);
                    if (during >= current) break;
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
