using System.Text.Json;
using Cocoar.Configuration.Providers.Tests.Helpers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Providers.Tests.File;

/// <summary>
/// Tests for FileSourceProvider behavior with multiple queries (multiple files from same provider instance)
/// Validates that debouncing happens per-file, not per-directory, and provider sharing works correctly.
/// </summary>
public class FileProviderMultiQueryTests
{
    private readonly ITestOutputHelper _output;

    public FileProviderMultiQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task SingleProvider_MultipleFiles_DebounceIndependently()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file1 = TempFileHelper.CreateInDirectory(tempDir.Path, "config1.json", """{"file": 1, "value": 0}""");
        using var file2 = TempFileHelper.CreateInDirectory(tempDir.Path, "config2.json", """{"file": 2, "value": 0}""");
        using var file3 = TempFileHelper.CreateInDirectory(tempDir.Path, "config3.json", """{"file": 3, "value": 0}""");

        // Single provider instance for the directory
        var options = new FileSourceProviderOptions(tempDir.Path);
        var provider = new FileSourceProvider(options);

        // Three separate queries for different files
        var query1 = new FileSourceProviderQueryOptions("config1.json", DebounceTime: TimeSpan.FromMilliseconds(50));
        var query2 = new FileSourceProviderQueryOptions("config2.json", DebounceTime: TimeSpan.FromMilliseconds(50));
        var query3 = new FileSourceProviderQueryOptions("config3.json", DebounceTime: TimeSpan.FromMilliseconds(50));

        var emissions1 = new EmissionLog();
        var emissions2 = new EmissionLog();
        var emissions3 = new EmissionLog();

        var subscription1 = provider.ChangesAsBytes(query1).Subscribe(e => emissions1.Add(e.ToJsonElement()));
        var subscription2 = provider.ChangesAsBytes(query2).Subscribe(e => emissions2.Add(e.ToJsonElement()));
        var subscription3 = provider.ChangesAsBytes(query3).Subscribe(e => emissions3.Add(e.ToJsonElement()));

        try
        {
            // Rapid changes to all 3 files simultaneously
            var changeCount = 10;
            for (var i = 1; i <= changeCount; i++)
            {
                // Change all files at nearly the same time
                file1.WriteJson(new { file = 1, value = i });
                file2.WriteJson(new { file = 2, value = i });
                file3.WriteJson(new { file = 3, value = i });
                await Task.Delay(10); // Rapid writes to test debouncing
            }

            // Wait for all file changes to be detected and for final debounced values to arrive
            await ActiveWaitHelpers.WaitUntilAsync(
                () => emissions1.Count > 0 && emissions2.Count > 0 && emissions3.Count > 0 &&
                      emissions1.Last.GetProperty("value").GetInt32() == changeCount &&
                      emissions2.Last.GetProperty("value").GetInt32() == changeCount &&
                      emissions3.Last.GetProperty("value").GetInt32() == changeCount,
                description: "final debounced values for all files");

            _output.WriteLine($"File 1: made {changeCount} changes, received {emissions1.Count} emissions");
            _output.WriteLine($"File 2: made {changeCount} changes, received {emissions2.Count} emissions");
            _output.WriteLine($"File 3: made {changeCount} changes, received {emissions3.Count} emissions");

            // Each file should have independent debouncing
            Assert.True(emissions1.Count < changeCount, $"File 1 should be debounced: expected < {changeCount}, got {emissions1.Count}");
            Assert.True(emissions2.Count < changeCount, $"File 2 should be debounced: expected < {changeCount}, got {emissions2.Count}");
            Assert.True(emissions3.Count < changeCount, $"File 3 should be debounced: expected < {changeCount}, got {emissions3.Count}");

            // All files should have at least one emission
            Assert.True(emissions1.Count > 0, "File 1 should have at least one emission");
            Assert.True(emissions2.Count > 0, "File 2 should have at least one emission");
            Assert.True(emissions3.Count > 0, "File 3 should have at least one emission");

            // Final emissions should reflect the last change for each file
            Assert.Equal(changeCount, emissions1.Last.GetProperty("value").GetInt32());
            Assert.Equal(changeCount, emissions2.Last.GetProperty("value").GetInt32());
            Assert.Equal(changeCount, emissions3.Last.GetProperty("value").GetInt32());

            // Validate file identity is preserved
            Assert.Equal(1, emissions1.Last.GetProperty("file").GetInt32());
            Assert.Equal(2, emissions2.Last.GetProperty("file").GetInt32());
            Assert.Equal(3, emissions3.Last.GetProperty("file").GetInt32());
        }
        finally
        {
            subscription1.Dispose();
            subscription2.Dispose();
            subscription3.Dispose();
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task SingleProvider_MultipleQueries_SameFile_ShareChangeStream()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "shared.json", """{"shared": true, "value": 0}""");

        var options = new FileSourceProviderOptions(tempDir.Path);
        var provider = new FileSourceProvider(options);

        // Two queries for the same file - should share the change stream
        var query1 = new FileSourceProviderQueryOptions("shared.json");
        var query2 = new FileSourceProviderQueryOptions("shared.json");

        var emissions1 = new EmissionLog();
        var emissions2 = new EmissionLog();

        var subscription1 = provider.ChangesAsBytes(query1).Subscribe(e => emissions1.Add(e.ToJsonElement()));
        var subscription2 = provider.ChangesAsBytes(query2).Subscribe(e => emissions2.Add(e.ToJsonElement()));

        try
        {
            // Make changes to the shared file
            for (var i = 1; i <= 5; i++)
            {
                file.WriteJson(new { shared = true, value = i });
                await Task.Delay(50); // Spaced writes
            }

            // Both queries end on the final value with the same number of emissions. One write can raise
            // several file-system events, and each event reaches the two subscribers one after the other,
            // so the counts are only comparable once the stream is quiet - which is what this waits for.
            await ActiveWaitHelpers.WaitUntilAsync(
                () => emissions1.Count > 0 && emissions1.Count == emissions2.Count &&
                      emissions1.Last.GetProperty("value").GetInt32() == 5 &&
                      emissions2.Last.GetProperty("value").GetInt32() == 5,
                description: "both queries on the final value with equal emission counts");

            _output.WriteLine($"Query 1: {emissions1.Count} emissions");
            _output.WriteLine($"Query 2: {emissions2.Count} emissions");

            // Both should have the same final value
            var final1 = emissions1.Last.GetProperty("value").GetInt32();
            var final2 = emissions2.Last.GetProperty("value").GetInt32();
            Assert.Equal(final1, final2);
            Assert.Equal(5, final1);
        }
        finally
        {
            subscription1.Dispose();
            subscription2.Dispose();
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task SingleProvider_TwoQueriesOnOneFileWithDifferentDebounce_BothReceiveTheFinalValue()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "throttle.json", """{"value": 0}""");

        var options = new FileSourceProviderOptions(tempDir.Path); // No provider-level debouncing
        var provider = new FileSourceProvider(options);

        // Same file, different per-query debounce settings
        var queryFast = new FileSourceProviderQueryOptions("throttle.json", DebounceTime: TimeSpan.FromMilliseconds(20));
        var querySlow = new FileSourceProviderQueryOptions("throttle.json", DebounceTime: TimeSpan.FromMilliseconds(100));

        var emissionsFast = new EmissionLog();
        var emissionsSlow = new EmissionLog();

        var subscriptionFast = provider.ChangesAsBytes(queryFast).Subscribe(e => emissionsFast.Add(e.ToJsonElement()));
        var subscriptionSlow = provider.ChangesAsBytes(querySlow).Subscribe(e => emissionsSlow.Add(e.ToJsonElement()));

        try
        {
            // Rapid changes
            for (var i = 1; i <= 10; i++)
            {
                file.WriteJson(new { value = i });
                await Task.Delay(10); // Rapid writes to test differential debouncing
            }

            // Wait for final debounced values to arrive
            await ActiveWaitHelpers.WaitUntilAsync(
                () => emissionsFast.Count > 0 && emissionsSlow.Count > 0 &&
                      emissionsFast.Last.GetProperty("value").GetInt32() == 10 &&
                      emissionsSlow.Last.GetProperty("value").GetInt32() == 10,
                description: "final debounced values for both queries");

            _output.WriteLine($"Fast query (20ms debounce): {emissionsFast.Count} emissions");
            _output.WriteLine($"Slow query (100ms debounce): {emissionsSlow.Count} emissions");

            // How many emissions each query sees depends on how the writes fall into the debounce windows,
            // so the counts are not compared. What must hold is that neither query misses the final state.
            // Both should have at least one emission
            Assert.True(emissionsFast.Count > 0, "Fast query should have emissions");
            Assert.True(emissionsSlow.Count > 0, "Slow query should have emissions");

            // Both should have the final value
            Assert.Equal(10, emissionsFast.Last.GetProperty("value").GetInt32());
            Assert.Equal(10, emissionsSlow.Last.GetProperty("value").GetInt32());
        }
        finally
        {
            subscriptionFast.Dispose();
            subscriptionSlow.Dispose();
        }
    }
}
