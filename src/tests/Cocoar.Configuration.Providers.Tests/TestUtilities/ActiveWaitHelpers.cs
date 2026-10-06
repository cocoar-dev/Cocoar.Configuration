namespace Cocoar.Configuration.Providers.Tests.TestUtilities;

public static class ActiveWaitHelpers
{
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        string description = "condition")
    {
        // The timeout only bounds a hung test. It is generous because a loaded CI runner can take
        // seconds to deliver a file-system event; a passing test returns as soon as the condition holds.
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
                // Condition threw (e.g., accessing property on incomplete JSON) - treat as "not yet met"
            }

            await Task.Delay(pollInterval);
        }
        
        throw new TimeoutException($"Timeout waiting for {description} after {timeout}");
    }
}
