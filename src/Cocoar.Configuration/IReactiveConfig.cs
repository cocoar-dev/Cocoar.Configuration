using System.Reactive.Subjects;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

/// <summary>
/// Represents a reactive configuration value that provides both observable updates 
/// and immediate access to the current value.
/// </summary>
/// <typeparam name="T">The configuration type</typeparam>
public interface IReactiveConfig<out T> : IObservable<T>
{
    /// <summary>
    /// Gets the current configuration value synchronously.
    /// </summary>
    T CurrentValue { get; }
}

/// <summary>
/// Error-resilient implementation of IReactiveConfig that ensures observables never terminate due to errors.
/// This implementation makes the observable streams bulletproof by catching and logging all errors
/// without terminating the stream.
/// </summary>
/// <typeparam name="T">The configuration type</typeparam>
internal sealed class ReactiveConfig<T> : IReactiveConfig<T>, IDisposable
{
    private readonly BehaviorSubject<T> _subject;
    private readonly ILogger _logger;
    private readonly IObservable<T> _errorResilientObservable;

    public ReactiveConfig(BehaviorSubject<T> subject, ILogger logger)
    {
        _subject = subject;
        _logger = logger;
        
        // Create error-resilient observable that never terminates due to errors
        _errorResilientObservable = _subject
            .AsObservable()
            .Catch<T, Exception>(ex =>
            {
                // Log the error but don't terminate the stream
                _logger.LogWarning(ex, 
                    "Error occurred in reactive config observable for type {Type}. " +
                    "The error will be ignored to keep the stream alive.", typeof(T));
                
                // Return an empty observable that doesn't emit anything
                // This allows the stream to continue without emitting invalid data
                return Observable.Empty<T>();
            })
            .Retry() // Automatically retry on any errors
            .Publish()
            .RefCount(); // Share the observable among multiple subscribers
    }

    /// <summary>
    /// Gets the current configuration value. This is always safe and will never throw.
    /// </summary>
    public T CurrentValue
    {
        get
        {
            try
            {
                return _subject.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Error getting current value for configuration type {Type}. Returning default value.", 
                    typeof(T));
                return default(T)!;
            }
        }
    }

    /// <summary>
    /// Subscribe to configuration changes. The returned observable is error-resilient and will
    /// never terminate due to subscriber errors or producer errors.
    /// </summary>
    public IDisposable Subscribe(IObserver<T> observer)
    {
        try
        {
            return _errorResilientObservable.Subscribe(
                onNext: value =>
                {
                    try
                    {
                        observer.OnNext(value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, 
                            "Error in subscriber callback for configuration type {Type}. " +
                            "The error will be ignored to keep the stream alive.", typeof(T));
                    }
                },
                onError: error =>
                {
                    // This should never be called due to our error handling above,
                    // but just in case, log it and don't propagate
                    _logger.LogError(error, 
                        "Unexpected error in reactive config stream for type {Type}.", typeof(T));
                },
                onCompleted: () =>
                {
                    try
                    {
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, 
                            "Error in subscriber OnCompleted for configuration type {Type}.", typeof(T));
                    }
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error creating subscription for configuration type {Type}.", typeof(T));
            
            // Return a dummy subscription that does nothing
            return System.Reactive.Disposables.Disposable.Empty;
        }
    }

    public void Dispose()
    {
        // Don't dispose the subject directly since it might be shared
        // The ConfigManager will handle subject disposal
    }
}

