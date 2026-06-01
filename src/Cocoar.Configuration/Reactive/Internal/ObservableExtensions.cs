namespace Cocoar.Configuration.Reactive.Internal;

internal static class ObservableExtensions
{
    public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext)
        => source.Subscribe(new ActionObserver<T>(onNext, null, null));

    public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext, Action<Exception> onError)
        => SubscribeSafe(source, new ActionObserver<T>(onNext, onError, null), onError);

    public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext, Action<Exception> onError, Action onCompleted)
        => SubscribeSafe(source, new ActionObserver<T>(onNext, onError, onCompleted), onError);

    /// <summary>
    /// Mirrors Rx's SubscribeSafe: catches exceptions thrown during Subscribe
    /// (e.g. BehaviorSubject initial replay) and routes them to onError.
    /// </summary>
    private static IDisposable SubscribeSafe<T>(IObservable<T> source, IObserver<T> observer, Action<Exception>? onError)
    {
        try
        {
            return source.Subscribe(observer);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return DisposableHelpers.Empty;
        }
    }

    public static IObservable<TResult> Select<T, TResult>(this IObservable<T> source, Func<T, TResult> selector)
        => new SelectObservable<T, TResult>(source, selector);

    public static IObservable<T> Where<T>(this IObservable<T> source, Func<T, bool> predicate)
        => new WhereObservable<T>(source, predicate);

    public static IObservable<T> DistinctUntilChanged<T>(this IObservable<T> source, IEqualityComparer<T> comparer)
        => new DistinctUntilChangedObservable<T>(source, comparer);

    private sealed class ActionObserver<T>(Action<T>? onNext, Action<Exception>? onError, Action? onCompleted) : IObserver<T>
    {
        public void OnNext(T value)
        {
            try
            {
                onNext?.Invoke(value);
            }
            catch (Exception ex) when (onError != null)
            {
                onError(ex);
            }
        }

        public void OnError(Exception error) => onError?.Invoke(error);
        public void OnCompleted() => onCompleted?.Invoke();
    }

    /// <summary>
    /// Mirrors Rx's source.SubscribeSafe(observer): if source.Subscribe throws
    /// (e.g. during BehaviorSubject initial replay), routes to observer.OnError.
    /// </summary>
    private static IDisposable SubscribeToSource<T>(IObservable<T> source, IObserver<T> observer)
    {
        try
        {
            return source.Subscribe(observer);
        }
        catch (Exception ex)
        {
            observer.OnError(ex);
            return DisposableHelpers.Empty;
        }
    }

    private sealed class SelectObservable<T, TResult>(IObservable<T> source, Func<T, TResult> selector) : IObservable<TResult>
    {
        public IDisposable Subscribe(IObserver<TResult> observer)
            => SubscribeToSource(source, new SelectObserver(observer, selector));

        private sealed class SelectObserver(IObserver<TResult> target, Func<T, TResult> selector) : IObserver<T>
        {
            public void OnNext(T value) => target.OnNext(selector(value));
            public void OnError(Exception error) => target.OnError(error);
            public void OnCompleted() => target.OnCompleted();
        }
    }

    private sealed class WhereObservable<T>(IObservable<T> source, Func<T, bool> predicate) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer)
            => SubscribeToSource(source, new WhereObserver(observer, predicate));

        private sealed class WhereObserver(IObserver<T> target, Func<T, bool> predicate) : IObserver<T>
        {
            public void OnNext(T value)
            {
                if (predicate(value)) target.OnNext(value);
            }

            public void OnError(Exception error) => target.OnError(error);
            public void OnCompleted() => target.OnCompleted();
        }
    }

    private sealed class DistinctUntilChangedObservable<T>(IObservable<T> source, IEqualityComparer<T> comparer) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer)
            => SubscribeToSource(source, new DistinctObserver(observer, comparer));

        private sealed class DistinctObserver(IObserver<T> target, IEqualityComparer<T> comparer) : IObserver<T>
        {
            private bool _hasValue;
            private T? _lastValue;

            public void OnNext(T value)
            {
                if (_hasValue && comparer.Equals(_lastValue!, value)) return;
                _hasValue = true;
                _lastValue = value;
                target.OnNext(value);
            }

            public void OnError(Exception error) => target.OnError(error);
            public void OnCompleted() => target.OnCompleted();
        }
    }
}
