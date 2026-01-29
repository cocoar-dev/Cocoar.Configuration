namespace Cocoar.Configuration.Reactive;

/// <summary>
/// Provides reactive access to configuration snapshots.
/// Emits new values whenever the underlying configuration changes, after debouncing and validation.
/// </summary>
public interface IReactiveConfig<out T> : IObservable<T>
{
    /// <summary>
    /// Gets the most recent configuration snapshot.
    /// Safe to call at any time - will not throw if configuration is temporarily unavailable.
    /// </summary>
    T CurrentValue { get; }
}
