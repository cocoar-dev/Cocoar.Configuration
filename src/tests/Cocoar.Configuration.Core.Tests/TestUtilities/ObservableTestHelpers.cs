using System.Reactive.Subjects;

namespace Cocoar.Configuration.Core.Tests.TestUtilities;

/// <summary>
/// Observable test helpers for bulletproof reactive testing.
/// Provides utilities for testing IObservable streams with proper active waiting patterns.
/// </summary>
public static class ObservableTestHelpers
{
    /// <summary>
    /// Wait for an observable to emit a value that matches the expected value within timeout.
    /// Uses active waiting pattern to avoid timing dependencies.
    /// </summary>
    /// <typeparam name="T">The type of values emitted by the observable</typeparam>
    /// <param name="source">The observable source to monitor</param>
    /// <param name="expectedValue">The expected value to wait for</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>The matched value</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task<T> WaitForValueAsync<T>(
        IObservable<T> source,
        T expectedValue,
        TimeSpan timeout = default,
        string description = "expected value")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        
        var completionSource = new TaskCompletionSource<T>();
        var subscription = source.Subscribe(value =>
        {
            if (EqualityComparer<T>.Default.Equals(value, expectedValue))
            {
                completionSource.TrySetResult(value);
            }
        }, completionSource.SetException);

        using var cancellation = new CancellationTokenSource(timeout);
        cancellation.Token.Register(() => 
            completionSource.TrySetException(new TimeoutException(
                $"Timeout waiting for {description}. Expected: {expectedValue}")));

        try
        {
            var result = await completionSource.Task;
            subscription.Dispose();
            return result;
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wait for an observable to emit a value that matches the specified predicate within timeout.
    /// Uses active waiting pattern to avoid timing dependencies.
    /// </summary>
    /// <typeparam name="T">The type of values emitted by the observable</typeparam>
    /// <param name="source">The observable source to monitor</param>
    /// <param name="predicate">Predicate to test emitted values</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>The matched value</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task<T> WaitForConditionAsync<T>(
        IObservable<T> source,
        Func<T, bool> predicate,
        TimeSpan timeout = default,
        string description = "condition")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        
        var completionSource = new TaskCompletionSource<T>();
        var subscription = source.Subscribe(value =>
        {
            if (predicate(value))
            {
                completionSource.TrySetResult(value);
            }
        }, completionSource.SetException);

        using var cancellation = new CancellationTokenSource(timeout);
        cancellation.Token.Register(() => 
            completionSource.TrySetException(new TimeoutException(
                $"Timeout waiting for {description}")));

        try
        {
            var result = await completionSource.Task;
            subscription.Dispose();
            return result;
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Verify that an observable emits a specific sequence of values in order.
    /// Uses active waiting with timeout for reliability.
    /// </summary>
    /// <typeparam name="T">The type of values emitted by the observable</typeparam>
    /// <param name="source">The observable source to monitor</param>
    /// <param name="expectedSequence">The expected sequence of values</param>
    /// <param name="timeout">Maximum time to wait for complete sequence (default: 15 seconds)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>Task that completes when sequence is verified</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task AssertSequenceAsync<T>(
        IObservable<T> source,
        T[] expectedSequence,
        TimeSpan timeout = default,
        string description = "sequence")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        
        if (expectedSequence.Length == 0)
        {
            throw new ArgumentException("Expected sequence cannot be empty", nameof(expectedSequence));
        }

        var emissions = new List<T>();
        var completionSource = new TaskCompletionSource<bool>();
        
        var subscription = source.Subscribe(value =>
        {
            emissions.Add(value);
            
            // Check if we have received the expected sequence
            if (emissions.Count >= expectedSequence.Length)
            {
                var matches = true;
                for (var i = 0; i < expectedSequence.Length; i++)
                {
                    if (!EqualityComparer<T>.Default.Equals(emissions[i], expectedSequence[i]))
                    {
                        matches = false;
                        break;
                    }
                }
                
                if (matches)
                {
                    completionSource.TrySetResult(true);
                }
            }
        }, completionSource.SetException);

        using var cancellation = new CancellationTokenSource(timeout);
        cancellation.Token.Register(() => 
            completionSource.TrySetException(new TimeoutException(
                $"Timeout waiting for {description}. Expected: [{string.Join(", ", expectedSequence)}], " +
                $"Got: [{string.Join(", ", emissions)}]")));

        try
        {
            await completionSource.Task;
            subscription.Dispose();
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wait for an observable to complete within the specified timeout.
    /// Uses active waiting pattern to avoid timing dependencies.
    /// </summary>
    /// <typeparam name="T">The type of values emitted by the observable</typeparam>
    /// <param name="source">The observable source to monitor</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>Task that completes when observable completes</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task WaitForCompletionAsync<T>(
        IObservable<T> source,
        TimeSpan timeout = default,
        string description = "completion")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        
        var completionSource = new TaskCompletionSource<bool>();
        var subscription = source.Subscribe(
            _ => { }, // OnNext - ignore values
            completionSource.SetException, // OnError
            () => completionSource.TrySetResult(true)); // OnCompleted

        using var cancellation = new CancellationTokenSource(timeout);
        cancellation.Token.Register(() => 
            completionSource.TrySetException(new TimeoutException(
                $"Timeout waiting for {description}")));

        try
        {
            await completionSource.Task;
            subscription.Dispose();
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wait for an observable to emit at least the specified number of values.
    /// Returns all emitted values up to that point.
    /// </summary>
    /// <typeparam name="T">The type of values emitted by the observable</typeparam>
    /// <param name="source">The observable source to monitor</param>
    /// <param name="count">Minimum number of emissions to wait for</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>List of all emitted values</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task<IReadOnlyList<T>> WaitForEmissionsAsync<T>(
        IObservable<T> source,
        int count,
        TimeSpan timeout = default,
        string description = "emissions")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        
        var emissions = new List<T>();
        var completionSource = new TaskCompletionSource<IReadOnlyList<T>>();
        
        var subscription = source.Subscribe(value =>
        {
            emissions.Add(value);
            if (emissions.Count >= count)
            {
                completionSource.TrySetResult(emissions.AsReadOnly());
            }
        }, completionSource.SetException);

        using var cancellation = new CancellationTokenSource(timeout);
        cancellation.Token.Register(() => 
            completionSource.TrySetException(new TimeoutException(
                $"Timeout waiting for {count} {description}. Got {emissions.Count}")));

        try
        {
            var result = await completionSource.Task;
            subscription.Dispose();
            return result;
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Create an observable test helper using BehaviorSubject.
    /// Perfect for testing scenarios where you need full control over emissions.
    /// </summary>
    /// <typeparam name="T">The type of values to emit</typeparam>
    /// <param name="initialValue">Initial value (optional)</param>
    /// <returns>BehaviorSubject for test control</returns>
    public static BehaviorSubject<T> CreateObservableSubject<T>(T? initialValue = default)
    {
        return initialValue is not null 
            ? new BehaviorSubject<T>(initialValue) 
            : throw new ArgumentException("BehaviorSubject requires an initial value. Use CreateEmptyObservableSubject<T>() if no initial value is desired.");
    }

    /// <summary>
    /// Create an observable test helper that starts without an initial value.
    /// Use this when you want to control exactly when the first emission occurs.
    /// </summary>
    /// <typeparam name="T">The type of values to emit</typeparam>
    /// <returns>Subject for test control</returns>
    public static Subject<T> CreateEmptyObservableSubject<T>()
    {
        return new();
    }
}
