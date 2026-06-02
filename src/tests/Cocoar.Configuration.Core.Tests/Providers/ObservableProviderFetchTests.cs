using System.Text;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Providers;

/// <summary>
/// Regression tests for <see cref="ObservableProvider{T}"/>.FetchConfigurationBytesAsync — the fetch path
/// must never hang and must always dispose its one-shot subscription.
/// </summary>
public class ObservableProviderFetchTests
{
    public record Cfg(string Name = "", int Value = 0);

    private static async Task<string> FetchJson<T>(IObservable<T> source)
    {
        var provider = new ObservableProvider<T>(new ObservableProviderOptions<T>(source));
        var bytes = await provider.FetchConfigurationBytesAsync(ObservableProviderQuery.Default);
        return Encoding.UTF8.GetString(bytes);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Fetch_does_not_hang_on_a_cold_source_that_never_emits()
    {
        using var cold = new Subject<Cfg>(); // never emits, never completes

        var fetch = FetchJson(cold);
        var finishedInTime = await Task.WhenAny(fetch, Task.Delay(2000)) == fetch;

        Assert.True(finishedInTime, "Fetch must not block on a source that has not emitted.");
        Assert.Equal("{}", await fetch); // empty snapshot; ChangesAsBytes delivers real values reactively
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Fetch_returns_empty_when_source_completes_without_emitting()
    {
        Assert.Equal("{}", await FetchJson(Observable.Empty<Cfg>()));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Fetch_takes_the_synchronously_replayed_value()
    {
        using var subject = new BehaviorSubject<Cfg>(new Cfg("hello", 42));
        var json = await FetchJson(subject);
        Assert.Contains("\"Name\":\"hello\"", json);
        Assert.Contains("\"Value\":42", json);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ObservableProvider")]
    public async Task Fetch_disposes_its_subscription_on_synchronous_replay()
    {
        var tracking = new TrackingObservable<Cfg>(new Cfg("x", 1));

        await FetchJson(tracking);

        Assert.Equal(0, tracking.ActiveSubscriptions); // disposed → no leak
    }

    /// <summary>An observable that replays a value synchronously on subscribe and tracks live subscriptions.</summary>
    private sealed class TrackingObservable<T>(T replayValue) : IObservable<T>
    {
        private int _active;
        public int ActiveSubscriptions => Volatile.Read(ref _active);

        public IDisposable Subscribe(IObserver<T> observer)
        {
            Interlocked.Increment(ref _active);
            observer.OnNext(replayValue); // synchronous emission, before Subscribe returns
            return new Unsubscriber(this);
        }

        private sealed class Unsubscriber(TrackingObservable<T> owner) : IDisposable
        {
            private int _disposed;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Interlocked.Decrement(ref owner._active);
                }
            }
        }
    }
}
