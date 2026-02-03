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
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch
            {
                // Condition threw (e.g., accessing property on incomplete JSON) - treat as "not yet met"
            }

            await Task.Delay(pollInterval);
        }
        
        throw new TimeoutException($"Timeout waiting for {description} after {timeout}");
    }
}
