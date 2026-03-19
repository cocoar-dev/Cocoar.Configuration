namespace Cocoar.Configuration.Reactive.Internal;

internal static class ObservableHelpers
{
    public static IObservable<T> Empty<T>() => EmptyObservable<T>.Instance;

    public static IObservable<T> Never<T>() => NeverObservable<T>.Instance;

    public static IObservable<T> Create<T>(Func<IObserver<T>, IDisposable> subscribeFactory)
        => new CreateObservable<T>(subscribeFactory);

    private sealed class EmptyObservable<T> : IObservable<T>
    {
        public static readonly EmptyObservable<T> Instance = new();

        public IDisposable Subscribe(IObserver<T> observer)
        {
            observer.OnCompleted();
            return DisposableHelpers.Empty;
        }
    }

    private sealed class NeverObservable<T> : IObservable<T>
    {
        public static readonly NeverObservable<T> Instance = new();

        public IDisposable Subscribe(IObserver<T> observer) => DisposableHelpers.Empty;
    }

    private sealed class CreateObservable<T>(Func<IObserver<T>, IDisposable> factory) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer) => factory(observer);
    }
}
