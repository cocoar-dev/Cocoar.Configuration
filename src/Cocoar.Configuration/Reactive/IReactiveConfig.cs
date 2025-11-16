using System.Reactive.Subjects;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;

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

internal static partial class ReactiveConfigLog
{
    [LoggerMessage(EventId = 6200, Level = LogLevel.Warning, Message = "Error occurred in reactive config observable for type {Type}. The error will be ignored to keep the stream alive.")]
    public static partial void ReactiveObservableError(this ILogger logger, Exception exception, Type Type);

    [LoggerMessage(EventId = 6201, Level = LogLevel.Warning, Message = "Error getting current value for configuration type {Type}. Returning default value.")]
    public static partial void GetCurrentValueError(this ILogger logger, Exception exception, Type Type);

    [LoggerMessage(EventId = 6202, Level = LogLevel.Warning, Message = "Error in subscriber callback for configuration type {Type}. The error will be ignored to keep the stream alive.")]
    public static partial void SubscriberCallbackError(this ILogger logger, Exception exception, Type Type);

    [LoggerMessage(EventId = 6203, Level = LogLevel.Error, Message = "Unexpected error in reactive config stream for type {Type}.")]
    public static partial void ReactiveStreamUnexpectedError(this ILogger logger, Exception exception, Type Type);

    [LoggerMessage(EventId = 6204, Level = LogLevel.Warning, Message = "Error in subscriber OnCompleted for configuration type {Type}.")]
    public static partial void SubscriberCompletedError(this ILogger logger, Exception exception, Type Type);

    [LoggerMessage(EventId = 6205, Level = LogLevel.Error, Message = "Error creating subscription for configuration type {Type}.")]
    public static partial void CreateSubscriptionError(this ILogger logger, Exception exception, Type Type);
}


internal sealed class ReactiveConfig<T> : IReactiveConfig<T>
{
    private readonly BehaviorSubject<T> _subject;
    private readonly ILogger _logger;
    private readonly IObservable<T> _errorResilientObservable;

    public ReactiveConfig(BehaviorSubject<T> subject, ILogger logger)
    {
        _subject = subject;
        _logger = logger;
        
        // Build error-resilient observable upfront rather than wrapping Subscribe() to avoid
        // per-subscription overhead and ensure consistent error handling across all subscribers
        _errorResilientObservable = _subject
            .AsObservable()
            .Catch<T, Exception>(ex =>
            {
                _logger.ReactiveObservableError(ex, typeof(T));
                return Observable.Empty<T>();
            })
            .Retry()
            .Publish()
            .RefCount();
    }

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
                _logger.GetCurrentValueError(ex, typeof(T));
                return default!;
            }
        }
    }

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
                        _logger.SubscriberCallbackError(ex, typeof(T));
                    }
                },
                onError: error =>
                {
                    _logger.ReactiveStreamUnexpectedError(error, typeof(T));
                },
                onCompleted: () =>
                {
                    try
                    {
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        _logger.SubscriberCompletedError(ex, typeof(T));
                    }
                }
            );
        }
        catch (Exception ex)
        {
            _logger.CreateSubscriptionError(ex, typeof(T));
            
            return System.Reactive.Disposables.Disposable.Empty;
        }
    }

}

