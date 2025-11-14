using System.Text.Json;
using Cocoar.Configuration.Providers.Tests.Helpers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Providers.Tests.File;

/// <summary>
/// Stress tests for FileSourceProvider in isolation (no ConfigManager).
/// Focus areas: debouncing, rapid file changes, concurrent access, file locking, change detection reliability.
/// </summary>
public class FileProviderStressTests
{
    private readonly ITestOutputHelper _output;

    public FileProviderStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task RapidFileChanges_WithDebouncing_CoalescesCorrectly()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "config.json", """{"value": 0}""");
        
        var options = new FileSourceProviderOptions(tempDir.Path);
        var query = new FileSourceProviderQueryOptions("config.json", DebounceTime: TimeSpan.FromMilliseconds(50));
        var provider = new FileSourceProvider(options);
        
        var emissions = new List<JsonElement>();
        var subscription = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()));
        
        try
        {
            // Rapid-fire 20 changes in quick succession
            var changeCount = 20;
            for (var i = 1; i <= changeCount; i++)
            {
                file.WriteJson(new { value = i });
                await Task.Delay(10); // Faster than debounce window
            }
            
            // Wait for debouncing to settle
            await Task.Delay(300);
            
            _output.WriteLine($"Made {changeCount} rapid changes, received {emissions.Count} emissions");
            
            // Debouncing should significantly reduce emissions
            Assert.True(emissions.Count < changeCount, $"Expected fewer than {changeCount} emissions due to debouncing, got {emissions.Count}");
            Assert.True(emissions.Count > 0, "Should have received at least one emission");
            
            // Final emission should reflect the last change
            var finalValue = emissions[^1].GetProperty("value").GetInt32();
            Assert.Equal(changeCount, finalValue);
        }
        finally
        {
            subscription.Dispose();
        }
    }

    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task HighFrequencyWrites_FileShareHandling_NoLockingErrors()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "highfreq.json", """{"counter": 0}""");
        
        var options = new FileSourceProviderOptions(tempDir.Path);
        var query = new FileSourceProviderQueryOptions("highfreq.json");
        var provider = new FileSourceProvider(options);
        
        var readErrors = 0;
        var successfulReads = 0;
        var emissions = new List<JsonElement>();
        
        var subscription = provider.ChangesAsBytes(query)
            .Subscribe(
                onNext: json => 
                {
                    emissions.Add(json.ToJsonElement());
                    Interlocked.Increment(ref successfulReads);
                },
                onError: ex => 
                {
                    _output.WriteLine($"Change stream error: {ex}");
                    Interlocked.Increment(ref readErrors);
                });
        
        try
        {
            // Simulate high-frequency writes from multiple "processes"
            var writeCount = 50;
            var writeTasks = new List<Task>();
            
            for (var i = 0; i < writeCount; i++)
            {
                var value = i;
                writeTasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(Random.Shared.Next(5, 25)); // Stagger writes
                    try
                    {
                        file.WriteJson(new { counter = value, timestamp = DateTime.UtcNow });
                    }
                    catch (IOException ex)
                    {
                        _output.WriteLine($"Write {value} failed: {ex.Message}");
                    }
                }));
            }
            
            await Task.WhenAll(writeTasks);
            
            // Wait for file system events to settle
            await Task.Delay(500);
            
            _output.WriteLine($"Completed {writeCount} writes, {successfulReads} successful reads, {readErrors} read errors, {emissions.Count} emissions");
            
            // FileShare.ReadWrite should prevent locking errors
            Assert.Equal(0, readErrors);
            Assert.True(successfulReads > 0, "Should have successfully read file changes");
            Assert.True(emissions.Count > 0, "Should have received change emissions");
        }
        finally
        {
            subscription.Dispose();
        }
    }

    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task ConcurrentFileAccess_MultipleProviders_SameFile()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "shared.json", """{"shared": true}""");
        
        var options = new FileSourceProviderOptions(tempDir.Path);
        var query = new FileSourceProviderQueryOptions("shared.json");
        
        // Create 5 providers accessing the same file concurrently
        var providerCount = 5;
        var providers = new List<FileSourceProvider>();
        var subscriptions = new List<IDisposable>();
        var allEmissions = new List<List<JsonElement>>();
        
        try
        {
            for (var i = 0; i < providerCount; i++)
            {
                var provider = new FileSourceProvider(options);
                providers.Add(provider);
                
                var emissions = new List<JsonElement>();
                allEmissions.Add(emissions);
                
                var subscription = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()));
                subscriptions.Add(subscription);
            }
            
            // Make several changes while all providers are watching
            var changes = 10;
            for (var i = 1; i <= changes; i++)
            {
                file.WriteJson(new { shared = true, iteration = i, timestamp = DateTime.UtcNow.Ticks });
                await Task.Delay(50);
            }
            
            await Task.Delay(200); // Let changes settle
            
            _output.WriteLine($"Made {changes} changes across {providerCount} concurrent providers");
            
            for (var i = 0; i < providerCount; i++)
            {
                var emissions = allEmissions[i];
                _output.WriteLine($"Provider {i}: {emissions.Count} emissions");
                Assert.True(emissions.Count > 0, $"Provider {i} should have received emissions");
                
                // Each provider should receive the changes independently
                if (emissions.Count > 0)
                {
                    var lastValue = emissions[^1].GetProperty("iteration").GetInt32();
                    Assert.True(lastValue > 0 && lastValue <= changes, 
                        $"Provider {i} final value {lastValue} should be within expected range");
                }
            }
        }
        finally
        {
            subscriptions.ForEach(s => s.Dispose());
        }
    }

    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task FileRecreation_DeleteAndRecreate_EmitsCorrectly()
    {
        using var tempDir = TempDirectoryHelper.Create();
        var fileName = "recreate.json";
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, fileName, """{"state": "initial"}""");
        
        var options = new FileSourceProviderOptions(tempDir.Path);
        var query = new FileSourceProviderQueryOptions(fileName);
        var provider = new FileSourceProvider(options);
        
        var emissions = new List<JsonElement>();
        var subscription = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()));
        
        try
        {
            // Initial modification
            file.WriteJson(new { state = "modified" });
            await Task.Delay(100);
            
            // Delete the file
            file.Delete();
            await Task.Delay(100);
            
            // Recreate with different content
            file.WriteJson(new { state = "recreated", newField = "added" });
            await Task.Delay(100);
            
            _output.WriteLine($"File recreation cycle completed, received {emissions.Count} emissions");
            
            Assert.True(emissions.Count >= 2, "Should detect both modification and recreation");
            
            // Find the last emission that contains the "state" property (skip any empty/deletion emissions)
            var finalEmission = emissions.LastOrDefault(e => e.TryGetProperty("state", out _));
            Assert.NotEqual(default, finalEmission);
            Assert.Equal("recreated", finalEmission.GetProperty("state").GetString());
            Assert.True(finalEmission.TryGetProperty("newField", out _), "Should contain new field after recreation");
        }
        finally
        {
            subscription.Dispose();
        }
    }

    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task MalformedJsonRecovery_BadThenGoodJson_HandlesgGracefully()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "recovery.json", """{"valid": true}""");
        
        var options = new FileSourceProviderOptions(tempDir.Path);
        var query = new FileSourceProviderQueryOptions("recovery.json");
        var provider = new FileSourceProvider(options);
        
        var emissions = new List<JsonElement>();
        var subscription = provider.ChangesAsBytes(query).Subscribe(e => emissions.Add(e.ToJsonElement()));
        
        try
        {
            // Write malformed JSON - should emit empty object
            file.WriteContent("{ invalid json here }");
            await Task.Delay(100);
            
            // Write valid JSON again
            file.WriteJson(new { recovered = true, value = 42 });
            await Task.Delay(100);
            
            _output.WriteLine($"JSON recovery test completed, received {emissions.Count} emissions");
            
            Assert.True(emissions.Count >= 2, "Should have emissions for both malformed and recovered JSON");
            
            // Check that malformed JSON resulted in empty object
            var malformedEmission = emissions[0];
            Assert.Equal(JsonValueKind.Object, malformedEmission.ValueKind);
            Assert.True(malformedEmission.EnumerateObject().MoveNext() == false, "Malformed JSON should result in empty object");
            
            // Check that recovery worked
            var recoveredEmission = emissions[^1];
            Assert.True(recoveredEmission.GetProperty("recovered").GetBoolean());
            Assert.Equal(42, recoveredEmission.GetProperty("value").GetInt32());
        }
        finally
        {
            subscription.Dispose();
        }
    }
}
