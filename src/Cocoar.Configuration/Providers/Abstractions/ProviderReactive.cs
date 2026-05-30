using Cocoar.Configuration.Reactive.Internal;

namespace Cocoar.Configuration.Providers.Abstractions;

/// <summary>
/// Factories for the change-notification <see cref="IObservable{T}"/> a custom provider returns from
/// <c>ChangesAsBytes</c>, without taking a dependency on System.Reactive. These are the same primitives the
/// built-in providers use. The <c>Provider</c> prefix avoids colliding with System.Reactive's <c>Observable</c>
/// for consumers that do reference it.
/// </summary>
public static class ProviderObservable
{
    /// <summary>An observable that never emits and never completes — use when a provider has no change detection.</summary>
    public static IObservable<T> Never<T>() => ObservableHelpers.Never<T>();

    /// <summary>An observable that completes immediately without emitting.</summary>
    public static IObservable<T> Empty<T>() => ObservableHelpers.Empty<T>();

    /// <summary>
    /// Creates an observable from a subscribe callback. The callback returns an <see cref="IDisposable"/>
    /// (see <see cref="ProviderDisposable"/>) that tears down any timers/tokens when the subscription ends.
    /// </summary>
    public static IObservable<T> Create<T>(Func<IObserver<T>, IDisposable> subscribe)
        => ObservableHelpers.Create(subscribe);
}

/// <summary>
/// Factories for the <see cref="IDisposable"/> a provider's change subscription returns from its subscribe callback.
/// </summary>
public static class ProviderDisposable
{
    /// <summary>A no-op disposable.</summary>
    public static IDisposable Empty => DisposableHelpers.Empty;

    /// <summary>A disposable that runs <paramref name="onDispose"/> exactly once when disposed.</summary>
    public static IDisposable Create(Action onDispose) => DisposableHelpers.Create(onDispose);
}
