namespace Cocoar.Configuration.Reactive.Internal;

internal static class DisposableHelpers
{
    public static readonly IDisposable Empty = new EmptyDisposable();

    public static IDisposable Create(Action dispose) => new ActionDisposable(dispose);

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class ActionDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
