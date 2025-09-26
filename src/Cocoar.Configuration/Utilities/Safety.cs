using System;
using System.Threading;

namespace Cocoar.Configuration.Utilities;

public static class Safety
{
    public static void DisposeQuietly(IDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }

        try
        {
            disposable.Dispose();
        }
        catch
        {
            // ignore dispose failures
        }
    }

    public static void CancelAndDisposeQuietly(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            // ignore cancellation failures
        }
        finally
        {
            DisposeQuietly(cts);
        }
    }

    public static void NotifyQuietly<T>(IObserver<T>? observer, T value)
    {
        if (observer is null)
        {
            return;
        }

        try
        {
            observer.OnNext(value);
        }
        catch
        {
            // ignore observer failures
        }
    }
}
