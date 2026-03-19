namespace Cocoar.Configuration.Reactive.Internal;

internal sealed class SimpleBehaviorSubject<T> : IObservable<T>, IObserver<T>, IDisposable
{
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif
    private IObserver<T>[] _observers = [];
    private T _value;
    private bool _disposed;

    public SimpleBehaviorSubject(T initialValue)
    {
        _value = initialValue;
    }

    public T Value
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _value;
            }
        }
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        T currentValue;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
#if NET9_0_OR_GREATER
            _observers = [.. _observers, observer];
#else
            _observers = _observers.Append(observer).ToArray();
#endif
            currentValue = _value;
        }

        observer.OnNext(currentValue);
        return DisposableHelpers.Create(() => RemoveObserver(observer));
    }

    public void OnNext(T value)
    {
        IObserver<T>[] snapshot;
        lock (_lock)
        {
            _value = value;
            snapshot = _observers;
        }

        foreach (var observer in snapshot)
            observer.OnNext(value);
    }

    public void OnError(Exception error)
    {
        IObserver<T>[] snapshot;
        lock (_lock) { snapshot = _observers; }
        foreach (var observer in snapshot)
            observer.OnError(error);
    }

    public void OnCompleted()
    {
        IObserver<T>[] snapshot;
        lock (_lock) { snapshot = _observers; }
        foreach (var observer in snapshot)
            observer.OnCompleted();
    }

    private void RemoveObserver(IObserver<T> observer)
    {
        lock (_lock)
        {
            _observers = _observers.Where(o => !ReferenceEquals(o, observer)).ToArray();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _observers = [];
        }
    }
}
