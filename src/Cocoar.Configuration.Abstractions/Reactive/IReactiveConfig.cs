namespace Cocoar.Configuration.Reactive;

/// <summary>
/// Provides reactive access to configuration snapshots.
/// Emits new values whenever the underlying configuration changes, after debouncing and validation.
/// </summary>
/// <remarks>
/// Implements BehaviorSubject / replay-1 semantics: calling <see cref="IObservable{T}.Subscribe"/>
/// immediately emits the current configuration value to the new subscriber, so subscribers never
/// miss the initial state regardless of when they subscribe.
/// </remarks>
public interface IReactiveConfig<out T> : IObservable<T>
{
    /// <summary>
    /// Gets the most recent configuration snapshot.
    /// Always reflects the last emitted value; never null after initialization completes.
    /// Safe to call at any time - will not throw if configuration is temporarily unavailable.
    /// </summary>
    T CurrentValue { get; }
}
