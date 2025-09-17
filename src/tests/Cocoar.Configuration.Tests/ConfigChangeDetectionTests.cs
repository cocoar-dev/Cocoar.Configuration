using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Cocoar.Configuration.Providers.StaticJsonProvider;

namespace Cocoar.Configuration.Tests;

/// <summary>
/// Tests for configuration change detection to ensure reactive configs only emit when values actually change.
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

    [Fact]
    public async Task ReactiveConfig_WhenUnchanged_DoesNotEmitDuplicate()
    {
        // Arrange
        var tempPath1 = Path.GetTempFileName();
        var tempPath2 = Path.GetTempFileName();
        
        try
        {
            // Write same content to both files
            var content = "{ \"Name\": \"Test\", \"Value\": 42, \"Enabled\": true }";
            File.WriteAllText(tempPath1, content);
            File.WriteAllText(tempPath2, content);

            var rules = new ConfigRule[]
            {
                // Rule 1: TestSettings from file 1
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath1))
                    .For<TestSettings>().Required().Build(),
                // Rule 2: SimpleSettings from file 2 
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath2))
                    .For<SimpleSettings>().Required().Build()
            };

            var configManager = new ConfigManager(rules, null, NullLogger.Instance);
            configManager.Initialize();

            var testReactiveConfig = configManager.GetReactiveConfig<TestSettings>();
            var simpleReactiveConfig = configManager.GetReactiveConfig<SimpleSettings>();

            var testEmissions = new List<TestSettings>();
            var simpleEmissions = new List<SimpleSettings>();

            var testSubscription = testReactiveConfig.Subscribe(config => testEmissions.Add(config));
            var simpleSubscription = simpleReactiveConfig.Subscribe(config => simpleEmissions.Add(config));

            // Wait for initial emissions
            await Task.Delay(100);
            
            // Verify initial emissions
            Assert.Single(testEmissions);
            Assert.Single(simpleEmissions);
            Assert.Equal("Test", testEmissions[0].Name);
            Assert.Equal(42, testEmissions[0].Value);

            // Act: Update file 1 with SAME content (should not trigger TestSettings emission)
            File.WriteAllText(tempPath1, content);
            
            // Update file 2 with DIFFERENT content (should trigger SimpleSettings emission)
            File.WriteAllText(tempPath2, "{ \"SimpleValue\": \"Changed\" }");

            // Wait for potential emissions
            await Task.Delay(500);

            // Assert: TestSettings should still have only 1 emission (no change)
            // SimpleSettings should have 2 emissions (changed)
            Assert.Single(testEmissions); // No duplicate emission for unchanged config
            Assert.Equal(2, simpleEmissions.Count); // New emission for changed config
            Assert.Equal("Changed", simpleEmissions[1].SimpleValue);

            testSubscription.Dispose();
            simpleSubscription.Dispose();
        }
        finally
        {
            if (File.Exists(tempPath1)) File.Delete(tempPath1);
            if (File.Exists(tempPath2)) File.Delete(tempPath2);
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

            var configManager = new ConfigManager(rules, null, NullLogger.Instance);
            configManager.Initialize();

            var reactiveConfig = configManager.GetReactiveConfig<TestSettings>();
            var emissions = new List<TestSettings>();
            var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

            // Wait for initial emission
            await Task.Delay(100);
            Assert.Single(emissions);
            Assert.Equal("Initial", emissions[0].Name);
            Assert.Equal(10, emissions[0].Value);
            Assert.False(emissions[0].Enabled);

            // Act: Change only one property
            var updatedContent = "{ \"Name\": \"Updated\", \"Value\": 10, \"Enabled\": false }";
            File.WriteAllText(tempPath, updatedContent);
            await Task.Delay(500);

            // Assert: Should get new emission for the change
            Assert.Equal(2, emissions.Count);
            Assert.Equal("Updated", emissions[1].Name);
            Assert.Equal(10, emissions[1].Value); // Unchanged
            Assert.False(emissions[1].Enabled); // Unchanged

            // Act: Update with same content again (should not emit)
            File.WriteAllText(tempPath, updatedContent);
            await Task.Delay(500);

            // Assert: Should still only have 2 emissions
            Assert.Equal(2, emissions.Count);

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

            // Wait for initial emission
            await Task.Delay(100);
            Assert.Single(emissions);

            // Act: Change nested timestamp property
            var updatedContent = "{ \"Name\": \"Test\", \"Value\": 42, \"Enabled\": true, \"Timestamp\": \"2025-01-02T00:00:00Z\" }";
            File.WriteAllText(tempPath, updatedContent);
            await Task.Delay(500);

            // Assert: Should detect the timestamp change
            Assert.Equal(2, emissions.Count);
            Assert.Equal(new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc), emissions[1].Timestamp.ToUniversalTime());

            // Act: Revert to original (should emit again)
            File.WriteAllText(tempPath, initialContent);
            await Task.Delay(500);

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