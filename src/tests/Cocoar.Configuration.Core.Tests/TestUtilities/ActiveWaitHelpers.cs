namespace Cocoar.Configuration.Core.Tests.TestUtilities;

/// <summary>
/// Active waiting helpers for bulletproof testing.
/// Provides utilities for condition-based waiting instead of fixed timing delays.
/// This is the foundation of reliable cross-platform testing.
/// </summary>
public static class ActiveWaitHelpers
{
    /// <summary>
    /// Wait for a condition to become true using active polling instead of fixed delays.
    /// This is the core pattern for bulletproof testing - never use Thread.Sleep or Task.Delay 
    /// for test synchronization!
    /// </summary>
    /// <param name="condition">Condition to check repeatedly</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="pollInterval">Interval between condition checks (default: 50ms)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>Task that completes when condition is true</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "condition")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch
            {
                // Condition threw (e.g., accessing property on incomplete state) - treat as "not yet met"
            }

            await Task.Delay(pollInterval);
        }

        throw new TimeoutException($"Timeout waiting for {description} after {timeout}");
    }

    /// <summary>
    /// Wait for an async condition to become true using active polling.
    /// Use this for conditions that require async operations to evaluate.
    /// </summary>
    /// <param name="condition">Async condition to check repeatedly</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="pollInterval">Interval between condition checks (default: 50ms)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>Task that completes when condition is true</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "async condition")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch
            {
                // Condition threw (e.g., accessing property on incomplete state) - treat as "not yet met"
            }

            await Task.Delay(pollInterval);
        }

        throw new TimeoutException($"Timeout waiting for {description} after {timeout}");
    }

    /// <summary>
    /// Wait for a value source to return a specific value.
    /// This is perfect for waiting for configuration updates, file changes, etc.
    /// </summary>
    /// <typeparam name="T">Type of value to check</typeparam>
    /// <param name="valueSource">Function that returns the current value</param>
    /// <param name="expectedValue">The expected value to wait for</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="pollInterval">Interval between checks (default: 50ms)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>Task that completes when value matches expected</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task WaitForValueAsync<T>(
        Func<T> valueSource,
        T expectedValue,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "expected value")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            var currentValue = valueSource();
            if (EqualityComparer<T>.Default.Equals(currentValue, expectedValue))
            {
                return;
            }

            await Task.Delay(pollInterval);
        }
        
        var finalValue = valueSource();
        throw new TimeoutException(
            $"Timeout waiting for {description} after {timeout}. " +
            $"Expected: {expectedValue}, Got: {finalValue}");
    }

    /// <summary>
    /// Wait for an async value source to return a specific value.
    /// Use this for async operations like configuration reads.
    /// </summary>
    /// <typeparam name="T">Type of value to check</typeparam>
    /// <param name="valueSource">Async function that returns the current value</param>
    /// <param name="expectedValue">The expected value to wait for</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="pollInterval">Interval between checks (default: 50ms)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>Task that completes when value matches expected</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task WaitForValueAsync<T>(
        Func<Task<T>> valueSource,
        T expectedValue,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "expected value")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            var currentValue = await valueSource();
            if (EqualityComparer<T>.Default.Equals(currentValue, expectedValue))
            {
                return;
            }

            await Task.Delay(pollInterval);
        }
        
        var finalValue = await valueSource();
        throw new TimeoutException(
            $"Timeout waiting for {description} after {timeout}. " +
            $"Expected: {expectedValue}, Got: {finalValue}");
    }

    /// <summary>
    /// Wait for a value to match a specific predicate condition.
    /// This is the most flexible waiting pattern for complex conditions.
    /// </summary>
    /// <typeparam name="T">Type of value to check</typeparam>
    /// <param name="valueSource">Function that returns the current value</param>
    /// <param name="predicate">Condition that must be satisfied</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="pollInterval">Interval between checks (default: 50ms)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>The value that satisfied the condition</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task<T> WaitForConditionAsync<T>(
        Func<T> valueSource,
        Func<T, bool> predicate,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "condition")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            var currentValue = valueSource();
            if (predicate(currentValue))
            {
                return currentValue;
            }

            await Task.Delay(pollInterval);
        }
        
        var finalValue = valueSource();
        throw new TimeoutException(
            $"Timeout waiting for {description} after {timeout}. Final value: {finalValue}");
    }

    /// <summary>
    /// Wait for an async value to match a specific predicate condition.
    /// </summary>
    /// <typeparam name="T">Type of value to check</typeparam>
    /// <param name="valueSource">Async function that returns the current value</param>
    /// <param name="predicate">Condition that must be satisfied</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="pollInterval">Interval between checks (default: 50ms)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>The value that satisfied the condition</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task<T> WaitForConditionAsync<T>(
        Func<Task<T>> valueSource,
        Func<T, bool> predicate,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "condition")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            var currentValue = await valueSource();
            if (predicate(currentValue))
            {
                return currentValue;
            }

            await Task.Delay(pollInterval);
        }
        
        var finalValue = await valueSource();
        throw new TimeoutException(
            $"Timeout waiting for {description} after {timeout}. Final value: {finalValue}");
    }

    /// <summary>
    /// Wait for a value to stabilize (remain unchanged for a period).
    /// Useful for waiting for debouncing to settle or operations to complete.
    /// </summary>
    /// <typeparam name="T">Type of value to monitor</typeparam>
    /// <param name="valueSource">Function that returns the current value</param>
    /// <param name="stabilityPeriod">How long value must remain stable (default: 200ms)</param>
    /// <param name="timeout">Maximum time to wait (default: 15 seconds)</param>
    /// <param name="pollInterval">Interval between checks (default: 50ms)</param>
    /// <param name="description">Description for debugging failures</param>
    /// <returns>The stable value</returns>
    /// <exception cref="TimeoutException">Thrown when timeout is exceeded</exception>
    public static async Task<T> WaitForStableValueAsync<T>(
        Func<T> valueSource,
        TimeSpan stabilityPeriod = default,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "stable value")
    {
        stabilityPeriod = stabilityPeriod == default ? TimeSpan.FromMilliseconds(200) : stabilityPeriod;
        timeout = timeout == default ? TimeSpan.FromSeconds(15) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stabilityStopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        T lastValue = valueSource();
        T currentValue;
        
        while (overallStopwatch.Elapsed < timeout)
        {
            await Task.Delay(pollInterval);
            currentValue = valueSource();
            
            if (EqualityComparer<T>.Default.Equals(currentValue, lastValue))
            {
                // Value is the same, check if we've been stable long enough
                if (stabilityStopwatch.Elapsed >= stabilityPeriod)
                {
                    return currentValue;
                }
            }
            else
            {
                // Value changed, reset stability timer
                lastValue = currentValue;
                stabilityStopwatch.Restart();
            }
        }
        
        throw new TimeoutException(
            $"Timeout waiting for {description} to stabilize after {timeout}");
    }
}
