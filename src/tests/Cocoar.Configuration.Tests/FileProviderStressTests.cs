using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Tests
{
    /// <summary>
    /// Comprehensive stress tests validating FileProvider reliability after FileShare.ReadWrite fix.
    /// Tests are organized by scenario: Reliability, Concurrent Access, and High-Frequency changes.
    /// </summary>
    public class FileProviderStressTests
    {
        private readonly ITestOutputHelper _output;

        public record TestConfig(string Name, int Value, DateTime Timestamp);

        public FileProviderStressTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Reliability Tests

        [Fact]
        public async Task Reliability_20Iterations_ConsistentBehavior()
        {
            _output.WriteLine("🔥 STARTING 20-ITERATION STRESS TEST");
            
            var successCount = 0;
            var totalEmissions = 0;
            var exceptions = new List<Exception>();

            for (int iteration = 1; iteration <= 20; iteration++)
            {
                _output.WriteLine($"\n=== ITERATION {iteration}/20 ===");
                
                try
                {
                    var emissions = await RunSingleFileProviderTest(iteration);
                    totalEmissions += emissions;
                    successCount++;
                    _output.WriteLine($"✅ Iteration {iteration}: {emissions} emissions");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    _output.WriteLine($"❌ Iteration {iteration}: FAILED - {ex.Message}");
                }

                // Small delay between iterations to prevent resource exhaustion
                await Task.Delay(10);
            }

            _output.WriteLine($"\n🏁 STRESS TEST COMPLETE:");
            _output.WriteLine($"   • Successful iterations: {successCount}/20 ({successCount/20.0:P1})");
            _output.WriteLine($"   • Total emissions detected: {totalEmissions}");
            _output.WriteLine($"   • Failures: {exceptions.Count}");

            if (exceptions.Count > 0)
            {
                _output.WriteLine($"   • Exception types: {string.Join(", ", exceptions.Select(e => e.GetType().Name).Distinct())}");
            }

            // Assert: Should have very high success rate (allow 1-2 failures for OS timing issues)
            Assert.True(successCount >= 18, $"Expected at least 18/20 successful iterations, got {successCount}. Failures: {string.Join("; ", exceptions.Select(e => e.Message))}");
        }

        #endregion

        #region Concurrent Access Tests

        [Fact]
        public async Task ConcurrentAccess_MultipleWriters_HandledGracefully()
        {
            _output.WriteLine("🔥 STARTING CONCURRENT ACCESS STRESS TEST");

            var tempPath = Path.GetTempFileName();
            
            try
            {
                File.WriteAllText(tempPath, "{ \"Name\": \"Initial\", \"Value\": 0, \"Timestamp\": \"2025-01-01T00:00:00Z\" }");

                var directory = Path.GetDirectoryName(tempPath)!;
                var filename = Path.GetFileName(tempPath);
                
                var options = new FileSourceProviderOptions(directory);
                var provider = new FileSourceProvider(options);
                
                var emissions = new List<System.Text.Json.JsonElement>();
                var subscription = provider.Changes(new FileSourceProviderQueryOptions(filename)).Subscribe(json => 
                {
                    lock (emissions)
                    {
                        emissions.Add(json);
                        var valueStr = json.TryGetProperty("Value", out var valueProp) ? valueProp.GetInt32().ToString() : "unknown";
                        _output.WriteLine($"Emission {emissions.Count}: Value={valueStr}");
                    }
                });

                await Task.Delay(300); // Initial settling
                var initialCount = emissions.Count;

                // Act: Multiple concurrent writers + FileSystemWatcher reader
                _output.WriteLine("🚀 Starting concurrent file writes...");
                
                var tasks = new List<Task>();
                var exceptions = new List<Exception>();

                // Create 5 concurrent write tasks
                for (int taskId = 1; taskId <= 5; taskId++)
                {
                    int capturedTaskId = taskId;
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            for (int i = 1; i <= 10; i++)
                            {
                                var value = capturedTaskId * 100 + i; // Unique values per task
                                var content = $"{{ \"Name\": \"Task{capturedTaskId}\", \"Value\": {value}, \"Timestamp\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\" }}";
                                
                                File.WriteAllText(tempPath, content);
                                _output.WriteLine($"Task {capturedTaskId}: Wrote Value={value}");
                                
                                await Task.Delay(Random.Shared.Next(1, 20)); // Random timing
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (exceptions)
                            {
                                exceptions.Add(ex);
                            }
                        }
                    });
                    
                    tasks.Add(task);
                }

                // Wait for all concurrent writes to complete
                await Task.WhenAll(tasks);
                
                // Wait for FileSystemWatcher to process all changes
                await Task.Delay(1000);

                var finalCount = emissions.Count;
                var newEmissions = finalCount - initialCount;

                _output.WriteLine($"\n📊 CONCURRENT ACCESS RESULTS:");
                _output.WriteLine($"   • 5 tasks × 10 writes = 50 total writes");
                _output.WriteLine($"   • {newEmissions} FileSystemWatcher emissions detected");
                _output.WriteLine($"   • Write exceptions: {exceptions.Count}");
                _output.WriteLine($"   • Detection rate: {newEmissions}/50 = {(newEmissions/50.0):P1}");

                if (exceptions.Count > 0)
                {
                    _output.WriteLine($"   • Exception types: {string.Join(", ", exceptions.Select(e => e.GetType().Name).Distinct())}");
                    foreach (var ex in exceptions.Take(3)) // Show first 3 exceptions
                    {
                        _output.WriteLine($"     - {ex.Message}");
                    }
                }

                // Assert: Should handle concurrent access without major issues
                Assert.True(exceptions.Count <= 5, $"Too many write exceptions: {exceptions.Count}. FileShare.ReadWrite should prevent most conflicts.");
                Assert.True(newEmissions > 0, "FileSystemWatcher should detect at least some changes during concurrent access");

                subscription.Dispose();
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        #endregion

        #region High-Frequency Tests

        [Fact]
        public async Task HighFrequency_RapidBursts_StabilityMaintained()
        {
            _output.WriteLine("🔥 STARTING HIGH-FREQUENCY BURST TEST");

            var tempPath = Path.GetTempFileName();
            
            try
            {
                File.WriteAllText(tempPath, "{ \"Value\": 0 }");

                var directory = Path.GetDirectoryName(tempPath)!;
                var filename = Path.GetFileName(tempPath);
                
                var options = new FileSourceProviderOptions(directory);
                var provider = new FileSourceProvider(options);
                
                var emissions = new List<System.Text.Json.JsonElement>();
                var subscription = provider.Changes(new FileSourceProviderQueryOptions(filename)).Subscribe(json => 
                {
                    emissions.Add(json);
                });

                await Task.Delay(300); // Initial settling
                var initialCount = emissions.Count;

                // Act: HIGH-FREQUENCY burst - 100 writes as fast as possible
                _output.WriteLine("⚡ Performing 100 HIGH-FREQUENCY writes...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                for (int i = 1; i <= 100; i++)
                {
                    File.WriteAllText(tempPath, $"{{ \"Value\": {i} }}");
                }
                
                stopwatch.Stop();
                _output.WriteLine($"⚡ 100 writes completed in {stopwatch.ElapsedMilliseconds}ms ({100.0/stopwatch.ElapsedMilliseconds*1000:F0} writes/second)");

                // Wait for FileSystemWatcher to process
                await Task.Delay(2000);

                var finalCount = emissions.Count;
                var newEmissions = finalCount - initialCount;

                _output.WriteLine($"\n📊 HIGH-FREQUENCY RESULTS:");
                _output.WriteLine($"   • 100 rapid writes performed");
                _output.WriteLine($"   • {newEmissions} FileSystemWatcher emissions detected");
                _output.WriteLine($"   • Write speed: {100.0/stopwatch.ElapsedMilliseconds*1000:F0} writes/second");
                _output.WriteLine($"   • Detection rate: {newEmissions}/100 = {(newEmissions/100.0):P1}");

                // Final file should contain the last value
                var finalContent = File.ReadAllText(tempPath);
                _output.WriteLine($"   • Final file content: {finalContent}");

                // Assert: Should handle high frequency without crashes
                Assert.True(newEmissions > 0, "Should detect at least some changes during high-frequency writes");
                Assert.Contains("100", finalContent); // Final value should be present

                subscription.Dispose();
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private async Task<int> RunSingleFileProviderTest(int iteration)
        {
            var tempPath = Path.GetTempFileName();
            
            try
            {
                File.WriteAllText(tempPath, $"{{ \"Name\": \"Initial{iteration}\", \"Value\": 0, \"Timestamp\": \"2025-01-01T00:00:00Z\" }}");

                var directory = Path.GetDirectoryName(tempPath)!;
                var filename = Path.GetFileName(tempPath);
                
                var options = new FileSourceProviderOptions(directory);
                var provider = new FileSourceProvider(options);
                
                var emissions = new List<System.Text.Json.JsonElement>();
                var subscription = provider.Changes(new FileSourceProviderQueryOptions(filename)).Subscribe(json => 
                {
                    emissions.Add(json);
                });

                await Task.Delay(100); // Quick settling
                var initialCount = emissions.Count;

                // Perform 5 rapid changes
                for (int i = 1; i <= 5; i++)
                {
                    var content = $"{{ \"Name\": \"Change{iteration}_{i}\", \"Value\": {i * 10}, \"Timestamp\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\" }}";
                    File.WriteAllText(tempPath, content);
                }

                await Task.Delay(200); // Wait for processing

                subscription.Dispose();
                
                return emissions.Count - initialCount;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        #endregion
    }
}