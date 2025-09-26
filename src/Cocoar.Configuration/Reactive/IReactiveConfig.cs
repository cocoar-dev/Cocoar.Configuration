using System.Reactive.Subjects;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Reactive;

public interface IReactiveConfig<out T> : IObservable<T>
{
    T CurrentValue { get; }
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
        
        _errorResilientObservable = _subject
            .AsObservable()
            .Catch<T, Exception>(ex =>
            {
                _logger.LogWarning(ex, 
                    "Error occurred in reactive config observable for type {Type}. " +
                    "The error will be ignored to keep the stream alive.", typeof(T));
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
                _logger.LogWarning(ex, 
                    "Error getting current value for configuration type {Type}. Returning default value.", 
                    typeof(T));
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
                        _logger.LogWarning(ex, 
                            "Error in subscriber callback for configuration type {Type}. " +
                            "The error will be ignored to keep the stream alive.", typeof(T));
                    }
                },
                onError: error =>
                {
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
            
            return System.Reactive.Disposables.Disposable.Empty;
        }
    }

}

