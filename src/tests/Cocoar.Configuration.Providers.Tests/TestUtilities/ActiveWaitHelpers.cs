namespace Cocoar.Configuration.Providers.Tests.TestUtilities;

public static class ActiveWaitHelpers
{
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "condition")
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(2) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(50) : pollInterval;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(pollInterval);
        }
        
        throw new TimeoutException($"Timeout waiting for {description} after {timeout}");
    }
}
