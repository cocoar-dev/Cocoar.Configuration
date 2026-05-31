namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>Minimal active-wait helper so this project stays self-contained (no cross-test-project reference).</summary>
internal static class TenantWait
{
    public static async Task UntilAsync(Func<bool> condition, string description, int timeoutMs = 15000, int pollMs = 25)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
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
                // condition touched not-yet-ready state — treat as "not met yet"
            }

            await Task.Delay(pollMs);
        }

        throw new TimeoutException($"Timeout waiting for {description} after {timeoutMs}ms");
    }
}
