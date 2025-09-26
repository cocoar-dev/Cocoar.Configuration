using System.Text.Json;
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
        var options = new FileSourceProviderOptions(tempDir.Path, debounceTime: TimeSpan.FromMilliseconds(100));
        var provider = new FileSourceProvider(options);

        // Three separate queries for different files
        var query1 = new FileSourceProviderQueryOptions("config1.json", DebounceTime: TimeSpan.FromMilliseconds(50));
        var query2 = new FileSourceProviderQueryOptions("config2.json", DebounceTime: TimeSpan.FromMilliseconds(50));
        var query3 = new FileSourceProviderQueryOptions("config3.json", DebounceTime: TimeSpan.FromMilliseconds(50));

        var emissions1 = new List<JsonElement>();
        var emissions2 = new List<JsonElement>();
        var emissions3 = new List<JsonElement>();

        var subscription1 = provider.Changes(query1).Subscribe(emissions1.Add);
        var subscription2 = provider.Changes(query2).Subscribe(emissions2.Add);
        var subscription3 = provider.Changes(query3).Subscribe(emissions3.Add);

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
                await Task.Delay(10); // Faster than debounce window
            }

            // Wait for all debouncing to settle
            await Task.Delay(300);

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
            Assert.Equal(changeCount, emissions1[^1].GetProperty("value").GetInt32());
            Assert.Equal(changeCount, emissions2[^1].GetProperty("value").GetInt32());
            Assert.Equal(changeCount, emissions3[^1].GetProperty("value").GetInt32());

            // Validate file identity is preserved
            Assert.Equal(1, emissions1[^1].GetProperty("file").GetInt32());
            Assert.Equal(2, emissions2[^1].GetProperty("file").GetInt32());
            Assert.Equal(3, emissions3[^1].GetProperty("file").GetInt32());
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

        var emissions1 = new List<JsonElement>();
        var emissions2 = new List<JsonElement>();

        var subscription1 = provider.Changes(query1).Subscribe(emissions1.Add);
        var subscription2 = provider.Changes(query2).Subscribe(emissions2.Add);

        try
        {
            // Make changes to the shared file
            for (var i = 1; i <= 5; i++)
            {
                file.WriteJson(new { shared = true, value = i });
                await Task.Delay(50);
            }

            await Task.Delay(100);

            _output.WriteLine($"Query 1: {emissions1.Count} emissions");
            _output.WriteLine($"Query 2: {emissions2.Count} emissions");

            // Both queries should receive the same number of emissions
            Assert.Equal(emissions1.Count, emissions2.Count);
            Assert.True(emissions1.Count > 0, "Should have received emissions");

            // Both should have the same final value
            var final1 = emissions1[^1].GetProperty("value").GetInt32();
            var final2 = emissions2[^1].GetProperty("value").GetInt32();
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
    public async Task SingleProvider_DifferentDebouncePerQuery_IndependentThrottling()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "throttle.json", """{"value": 0}""");

        var options = new FileSourceProviderOptions(tempDir.Path); // No provider-level debouncing
        var provider = new FileSourceProvider(options);

        // Same file, different per-query debounce settings
        var queryFast = new FileSourceProviderQueryOptions("throttle.json", DebounceTime: TimeSpan.FromMilliseconds(20));
        var querySlow = new FileSourceProviderQueryOptions("throttle.json", DebounceTime: TimeSpan.FromMilliseconds(100));

        var emissionsFast = new List<JsonElement>();
        var emissionsSlow = new List<JsonElement>();

        var subscriptionFast = provider.Changes(queryFast).Subscribe(emissionsFast.Add);
        var subscriptionSlow = provider.Changes(querySlow).Subscribe(emissionsSlow.Add);

        try
        {
            // Rapid changes
            for (var i = 1; i <= 10; i++)
            {
                file.WriteJson(new { value = i });
                await Task.Delay(10); // Faster than both debounce windows
            }

            // Wait for all debouncing to complete
            await Task.Delay(200);

            _output.WriteLine($"Fast query (20ms debounce): {emissionsFast.Count} emissions");
            _output.WriteLine($"Slow query (100ms debounce): {emissionsSlow.Count} emissions");

            // Fast query should have more emissions than slow query
            // Both should be debounced but fast should be less aggressive
            Assert.True(emissionsFast.Count >= emissionsSlow.Count, 
                "Fast query should have >= emissions than slow query due to different debounce windows");
            
            // Both should have at least one emission
            Assert.True(emissionsFast.Count > 0, "Fast query should have emissions");
            Assert.True(emissionsSlow.Count > 0, "Slow query should have emissions");

            // Both should have the final value
            Assert.Equal(10, emissionsFast[^1].GetProperty("value").GetInt32());
            Assert.Equal(10, emissionsSlow[^1].GetProperty("value").GetInt32());
        }
        finally
        {
            subscriptionFast.Dispose();
            subscriptionSlow.Dispose();
        }
    }
}
