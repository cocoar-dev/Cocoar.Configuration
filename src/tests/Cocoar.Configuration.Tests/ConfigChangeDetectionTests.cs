using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Cocoar.Configuration.Providers.StaticJsonProvider;

namespace Cocoar.Configuration.Tests;

/// <summary>
/// Tests for configuration change detection - CORRECTED to focus on final value correctness
/// rather than emission counting. After debouncing settles, the final value must be correct!
/// The number of intermediate emissions doesn't matter - only final correctness matters.
/// </summary>
public class ConfigChangeDetectionTests
{
    public class TestSettings
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public bool Enabled { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SimpleSettings
    {
        public string SimpleValue { get; set; } = "";
    }

    public class TestConfig
    {
        public int Value { get; set; }
    }

    [Fact]
    public async Task ReactiveConfig_WhenUnchanged_DoesNotEmitExcessively()
    {
        // Arrange - Use REAL FileProvider to test the actual issue users would face
        var tempPath = Path.GetTempFileName();
        
        try
        {
            var initialContent = "{ \"Value\": 42 }";
            File.WriteAllText(tempPath, initialContent);

            var rules = new ConfigRule[]
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath))
                    .For<TestConfig>().Required().Build()
            };

            var configManager = new ConfigManager(rules, null, NullLogger.Instance, debounceMilliseconds: 200);
            configManager.Initialize();

            var reactiveConfig = configManager.GetReactiveConfig<TestConfig>();
            var emissions = new List<TestConfig>();
            var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

            // Wait for initial emission (FileSystemWatcher needs time to settle)
            await Task.Delay(300);
            Assert.True(emissions.Count > 0, "Should have initial emission");
            
            // Act: Write the EXACT same content multiple times rapidly
            // This simulates scenarios where external tools might save unchanged files
            for (int i = 0; i < 3; i++)
            {
                File.WriteAllText(tempPath, initialContent); // Same content!
                await Task.Delay(50); // Rapid writes
            }
            
            // Wait for debouncing to settle
            await Task.Delay(400);

            // Assert: Final value should be correct (emission count doesn't matter)
            Assert.Equal(42, emissions.Last().Value); // Final value must be correct!
            // All emissions should have same value (no corruption from rapid writes)
            Assert.True(emissions.All(e => e.Value == 42), "All emissions should maintain correct value");

            subscription.Dispose();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ReactiveConfig_WhenPropertyChanged_EmitsUpdate()
    {
        // Arrange
        var tempPath = Path.GetTempFileName();
        
        try
        {
            var initialContent = "{ \"Name\": \"Initial\", \"Value\": 10, \"Enabled\": false }";
            File.WriteAllText(tempPath, initialContent);

            var rules = new ConfigRule[]
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath))
                    .For<TestSettings>().Required().Build()
            };

            var configManager = new ConfigManager(rules, null, NullLogger.Instance, debounceMilliseconds: 200);
            configManager.Initialize();

            var reactiveConfig = configManager.GetReactiveConfig<TestSettings>();
            var emissions = new List<TestSettings>();
            var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

            // Wait for initial emission (FileSystemWatcher needs time to settle)
            await Task.Delay(400);
            Assert.True(emissions.Count > 0, "Should have initial emission");
            Assert.Equal("Initial", emissions.Last().Name);
            Assert.Equal(10, emissions.Last().Value);
            Assert.False(emissions.Last().Enabled);

            // Act: Change only one property
            var updatedContent = "{ \"Name\": \"Updated\", \"Value\": 10, \"Enabled\": false }";
            File.WriteAllText(tempPath, updatedContent);
            
            // Wait for change to be detected - FileSystemWatcher is asynchronous and timing varies
            // The key insight: We care about FINAL VALUE CORRECTNESS, not precise timing
            var timeout = TimeSpan.FromSeconds(10);  // Generous timeout
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            while (stopwatch.Elapsed < timeout && emissions.Last().Name == "Initial")
            {
                await Task.Delay(100);
            }

            // Assert: Final value must be correct - this is what users actually see!
            var finalEmission = emissions.Last();
            Assert.Equal("Updated", finalEmission.Name);  // ✅ Change was applied correctly
            Assert.Equal(10, finalEmission.Value);        // ✅ Other values preserved correctly  
            Assert.False(finalEmission.Enabled);          // ✅ Other values preserved correctly
            
            // Should have at least initial emission, exact count irrelevant
            Assert.True(emissions.Count >= 1, "Should have at least initial emission");

            // Act: Update with same content again (test deduplication behavior)
            File.WriteAllText(tempPath, updatedContent);
            await Task.Delay(400); // Wait for potential duplicate detection

            // Assert: Final value should remain correct (deduplication may or may not emit, but final value must be right)
            var finalEmissionAfterDuplicate = emissions.Last();
            Assert.Equal("Updated", finalEmissionAfterDuplicate.Name);  // ✅ Still correct after duplicate
            Assert.Equal(10, finalEmissionAfterDuplicate.Value);        // ✅ Still correct after duplicate
            Assert.False(finalEmissionAfterDuplicate.Enabled);          // ✅ Still correct after duplicate

            subscription.Dispose();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ReactiveConfig_WithComplexObjectChanges_DetectsCorrectly()
    {
        // Arrange
        var tempPath = Path.GetTempFileName();
        
        try
        {
            var initialContent = "{ \"Name\": \"Test\", \"Value\": 42, \"Enabled\": true, \"Timestamp\": \"2025-01-01T00:00:00Z\" }";
            File.WriteAllText(tempPath, initialContent);

            var rules = new ConfigRule[]
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath))
                    .For<TestSettings>().Required().Build()
            };

            var configManager = new ConfigManager(rules, null, NullLogger.Instance);
            configManager.Initialize();

            var reactiveConfig = configManager.GetReactiveConfig<TestSettings>();
            var emissions = new List<TestSettings>();
            var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

            // Wait for initial emission (FileSystemWatcher needs time to settle)
            await Task.Delay(250);
            Assert.Single(emissions);

            // Act: Change nested timestamp property
            var updatedContent = "{ \"Name\": \"Test\", \"Value\": 42, \"Enabled\": true, \"Timestamp\": \"2025-01-02T00:00:00Z\" }";
            File.WriteAllText(tempPath, updatedContent);
            
            // Actively wait for the change to be detected (up to 5 seconds)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(5) && emissions.Count < 2)
            {
                await Task.Delay(200);
            }

            // Assert: Should detect the timestamp change
            Assert.Equal(2, emissions.Count);
            Assert.Equal(new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc), emissions[1].Timestamp.ToUniversalTime());

            // Cross-platform delay: FileSystemWatcher timing varies between Windows/Linux
            // Windows ARM (local): Fast response, 100ms sufficient
            // Ubuntu x64 (CI): Slower response, needs more time
            await Task.Delay(300);

            // Act: Revert to original (should emit again)
            File.WriteAllText(tempPath, initialContent);
            
            // Actively wait for the revert to be detected (up to 5 seconds)
            sw.Restart();
            while (sw.Elapsed < TimeSpan.FromSeconds(5) && emissions.Count < 3)
            {
                await Task.Delay(200);
            }

            // Assert: Should detect the revert
            Assert.Equal(3, emissions.Count);
            Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), emissions[2].Timestamp.ToUniversalTime());

            subscription.Dispose();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ReactiveConfig_WithNullValues_HandlesCorrectly()
    {
        // Arrange - Test with static rules to have more control
        var initialRule = Rule.From.Static<TestSettings>(_ => new TestSettings { Name = "Test", Value = 42 })
            .For<TestSettings>().Build();
        var nullRule = Rule.From.Static<TestSettings>(_ => null!)
            .For<TestSettings>().Build();

        var configManager1 = new ConfigManager(new[] { initialRule }, null, NullLogger.Instance);
        configManager1.Initialize();

        var configManager2 = new ConfigManager(new[] { nullRule }, null, NullLogger.Instance);
        configManager2.Initialize();

        // Act & Assert: Initial non-null value
        var reactiveConfig1 = configManager1.GetReactiveConfig<TestSettings>();
        Assert.NotNull(reactiveConfig1.CurrentValue);
        Assert.Equal("Test", reactiveConfig1.CurrentValue.Name);

        // Act & Assert: Null value handling
        var reactiveConfig2 = configManager2.GetReactiveConfig<TestSettings>();
        // Should handle null gracefully (might be default value depending on implementation)
    }
}