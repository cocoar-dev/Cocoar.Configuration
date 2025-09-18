using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Tests
{
    /// <summary>
    /// ISOLATE FileProvider to understand WHY FileSystemWatcher is unreliable.
    /// These tests help us understand the root cause and potentially fix it.
    /// </summary>
    public class FileProviderIsolationTests
    {
        private readonly ITestOutputHelper _output;

        public record TestSettings(string Name, int Value, bool Enabled, DateTime Timestamp);

        public FileProviderIsolationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task FileProvider_DirectChanges_DetectionReliability()
        {
            // Arrange: Test FileProvider directly (no ConfigManager)
            var tempPath = Path.GetTempFileName();
            
            try
            {
                var initialContent = "{ \"Name\": \"Test\", \"Value\": 42, \"Enabled\": true, \"Timestamp\": \"2025-01-01T00:00:00Z\" }";
                File.WriteAllText(tempPath, initialContent);

                // FileSourceProvider needs directory and filename separately  
                var directory = Path.GetDirectoryName(tempPath)!;
                var filename = Path.GetFileName(tempPath);
                
                var options = new FileSourceProviderOptions(directory);
                var provider = new FileSourceProvider(options);
                
                var query = new FileSourceProviderQueryOptions(filename);
                var emissions = new List<System.Text.Json.JsonElement>();
                
                // Subscribe directly to provider's Changes observable
                var subscription = provider.Changes(query).Subscribe(json => 
                {
                    emissions.Add(json);
                    _output.WriteLine($"FileProvider emission at {DateTime.Now:HH:mm:ss.fff}: {json}");
                });

                // Wait for initial settling
                await Task.Delay(500);
                var initialCount = emissions.Count;
                _output.WriteLine($"Initial emissions: {initialCount}");

                // Act: Change file content
                var updatedContent = "{ \"Name\": \"Test\", \"Value\": 42, \"Enabled\": true, \"Timestamp\": \"2025-01-02T00:00:00Z\" }";
                _output.WriteLine($"Writing updated content at {DateTime.Now:HH:mm:ss.fff}...");
                File.WriteAllText(tempPath, updatedContent);

                // Wait and observe FileSystemWatcher behavior
                _output.WriteLine("Waiting for FileSystemWatcher to detect change...");
                await Task.Delay(2000); // Give plenty of time

                var finalCount = emissions.Count;
                _output.WriteLine($"Final emissions: {finalCount}");

                // Analysis
                if (finalCount > initialCount)
                {
                    _output.WriteLine("✅ FileSystemWatcher detected the change!");
                    _output.WriteLine($"Additional emissions: {finalCount - initialCount}");
                }
                else
                {
                    _output.WriteLine("❌ FileSystemWatcher FAILED to detect the change!");
                    _output.WriteLine("This is the root cause of the flaky tests.");
                }

                subscription.Dispose();
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task FileProvider_MultipleRapidWrites_BehaviorAnalysis()
        {
            // Arrange: Test how FileProvider handles rapid writes
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
                    _output.WriteLine($"Emission {emissions.Count}: {json}");
                });

                await Task.Delay(500); // Initial settling
                var initialCount = emissions.Count;

                // Act: Rapid writes (like our 100 changes test, but with files)
                _output.WriteLine("🚀 Performing 10 rapid file writes...");
                for (int i = 1; i <= 10; i++)
                {
                    File.WriteAllText(tempPath, $"{{ \"Value\": {i} }}");
                    _output.WriteLine($"  Write {i} at {DateTime.Now:HH:mm:ss.fff}");
                }

                // Wait for all potential emissions
                await Task.Delay(1000);

                var finalCount = emissions.Count;
                var newEmissions = finalCount - initialCount;

                _output.WriteLine($"📊 RESULTS:");
                _output.WriteLine($"   • 10 rapid writes performed");
                _output.WriteLine($"   • {newEmissions} FileSystemWatcher emissions detected");
                _output.WriteLine($"   • Detection rate: {newEmissions}/10 = {(newEmissions/10.0):P1}");

                if (newEmissions == 0)
                {
                    _output.WriteLine("❌ FileSystemWatcher completely missed rapid changes");
                }
                else if (newEmissions < 10)
                {
                    _output.WriteLine($"⚠️ FileSystemWatcher missed {10 - newEmissions} changes");
                }
                else
                {
                    _output.WriteLine("✅ FileSystemWatcher detected all changes (surprising!)");
                }

                subscription.Dispose();
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task FileProvider_WithPolling_ReliabilityComparison()
        {
            // Arrange: Test FileProvider with polling backup
            var tempPath = Path.GetTempFileName();
            
            try
            {
                File.WriteAllText(tempPath, "{ \"Value\": 0 }");

                var directory = Path.GetDirectoryName(tempPath)!;
                var filename = Path.GetFileName(tempPath);
                
                var options = new FileSourceProviderOptions(directory, TimeSpan.FromMilliseconds(100)); // Enable polling backup
                var provider = new FileSourceProvider(options);
                
                var emissions = new List<System.Text.Json.JsonElement>();
                var subscription = provider.Changes(new FileSourceProviderQueryOptions(filename)).Subscribe(json => 
                {
                    emissions.Add(json);
                    _output.WriteLine($"Polling-backed emission: {json}");
                });

                await Task.Delay(300); // Initial settling
                var initialCount = emissions.Count;

                // Act: Single change (should be caught by polling if FileSystemWatcher fails)
                _output.WriteLine("Writing change with polling backup enabled...");
                File.WriteAllText(tempPath, "{ \"Value\": 99 }");

                // Wait for either FileSystemWatcher or polling to detect
                await Task.Delay(500); // > polling interval

                var finalCount = emissions.Count;
                _output.WriteLine($"Emissions with polling backup: {finalCount - initialCount}");

                if (finalCount > initialCount)
                {
                    _output.WriteLine("✅ Change detected (either FileSystemWatcher or polling)");
                    _output.WriteLine("💡 Polling can provide reliability backup for FileSystemWatcher");
                }
                else
                {
                    _output.WriteLine("❌ Even with polling backup, change was missed!");
                }

                subscription.Dispose();
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}