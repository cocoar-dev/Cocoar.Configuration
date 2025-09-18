using System.Reactive.Subjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.ObservableProvider;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Tests
{
    /// <summary>
    /// Tests for rapid change handling and debouncing behavior.
    /// 
    /// Validates that ConfigManager can handle rapid changes correctly:
    /// - Debounces multiple rapid changes into fewer emissions
    /// - Maintains correct final values despite rapid changes
    /// - Proves ObservableProvider vs ConfigManager behavioral differences
    /// </summary>
    public class RapidChangeHandlingTests
    {
        private readonly ITestOutputHelper _output;

        public record TestConfig(string Name, int Value, bool Enabled);

        public RapidChangeHandlingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ConfigManager_WithRapidChanges_DebouncesCorrectly()
        {
            // Arrange: Create BehaviorSubject for perfect control
            var behaviorSubject = new BehaviorSubject<TestConfig>(
                new TestConfig("Start", 0, false)
            );
            
            var rules = new ConfigRule[]
            {
                Rule.From.Controllable(behaviorSubject).For<TestConfig>().Required().Build()
            };

            // ConfigManager with debouncing (this should coalesce rapid changes)
            var configManager = new ConfigManager(rules, null, NullLogger.Instance, debounceMilliseconds: 50);
            configManager.Initialize();

            var reactiveConfig = configManager.GetReactiveConfig<TestConfig>();
            var emissions = new List<TestConfig>();
            var subscription = reactiveConfig.Subscribe(config => 
            {
                emissions.Add(config);
                _output.WriteLine($"ConfigManager Emission #{emissions.Count}: Value={config.Value} at {DateTime.Now:HH:mm:ss.fff}");
            });

            var initialEmissionCount = emissions.Count;
            _output.WriteLine($"Initial emissions from setup: {initialEmissionCount}");

            // Act: 100 RAPID changes! 💥
            _output.WriteLine("🚀 FIRING 100 RAPID CHANGES...");
            var startTime = DateTime.Now;
            
            for (int i = 1; i <= 100; i++)
            {
                behaviorSubject.OnNext(new TestConfig($"Change{i}", i, i % 2 == 0));
            }
            
            var endTime = DateTime.Now;
            _output.WriteLine($"⚡ All 100 changes fired in {(endTime - startTime).TotalMilliseconds:F1}ms");

            // Wait for ConfigManager debouncing to settle
            Thread.Sleep(200); // > debounce window to ensure settling

            // Assert: Debouncing theory validation
            var totalEmissions = emissions.Count;
            var finalValue = emissions.Last();

            _output.WriteLine($"📊 RESULTS:");
            _output.WriteLine($"   • Initial emissions: {initialEmissionCount}");
            _output.WriteLine($"   • Total emissions after 100 changes: {totalEmissions}");
            _output.WriteLine($"   • New emissions from changes: {totalEmissions - initialEmissionCount}");
            _output.WriteLine($"   • Final value: {finalValue.Value}");
            _output.WriteLine($"   • Final name: {finalValue.Name}");
            _output.WriteLine($"   • Final enabled: {finalValue.Enabled}");

            // 🎯 CORE THEORY VALIDATION:
            
            // 1. Should be FAR fewer than 100 emissions (debouncing working!)
            Assert.True(totalEmissions < 50, 
                $"Expected heavy debouncing! Got {totalEmissions} emissions from 100 changes. ConfigManager debouncing should coalesce rapid changes.");
                
            // 2. Final value must be absolutely correct (Value = 100)
            Assert.Equal(100, finalValue.Value); // ✅ Must be the 100th change
            Assert.Equal("Change100", finalValue.Name); // ✅ Must be the 100th change name
            Assert.True(finalValue.Enabled); // ✅ 100 % 2 == 0, so should be true

            // 3. Should have some emissions (not zero - debouncing, not blocking)
            Assert.True(totalEmissions > initialEmissionCount, 
                "Should have at least some emissions from the 100 changes");

            _output.WriteLine($"✅ THEORY CONFIRMED:");
            _output.WriteLine($"   • 100 rapid changes were debounced down to {totalEmissions - initialEmissionCount} emissions");
            _output.WriteLine($"   • Final result is 100% correct (Value=100, Name=Change100, Enabled=true)");
            _output.WriteLine($"   • ConfigManager debouncing is working as designed! 🎉");

            subscription.Dispose();
            behaviorSubject.Dispose();
        }

        [Fact]
        public void ObservableProvider_WithRapidChanges_EmitsAll()
        {
            // Arrange: Test ObservableProvider directly (no ConfigManager debouncing)
            var behaviorSubject = new BehaviorSubject<TestConfig>(
                new TestConfig("Start", 0, false)
            );
            
            var provider = new ObservableProvider<TestConfig>(
                new ObservableProviderOptions<TestConfig>(behaviorSubject)
            );
            
            var emissions = new List<System.Text.Json.JsonElement>();
            var subscription = provider.Changes(ObservableProviderQuery.Default)
                .Subscribe(json => emissions.Add(json));

            var initialCount = emissions.Count;

            // Act: Same 100 rapid changes
            _output.WriteLine("🚀 FIRING 100 CHANGES DIRECTLY TO OBSERVABLE PROVIDER...");
            
            for (int i = 1; i <= 100; i++)
            {
                behaviorSubject.OnNext(new TestConfig($"Change{i}", i, i % 2 == 0));
            }

            Thread.Sleep(10); // Minimal wait

            // Assert: ObservableProvider should emit ALL 100 changes (no debouncing)
            var totalEmissions = emissions.Count;
            _output.WriteLine($"📊 ObservableProvider Results:");
            _output.WriteLine($"   • Initial: {initialCount}, Total: {totalEmissions}");
            _output.WriteLine($"   • New emissions: {totalEmissions - initialCount}");

            // Should be ALL 100 changes + initial = 101 total
            Assert.Equal(101, totalEmissions); // Initial + all 100 changes ✅
            
            _output.WriteLine($"✅ CONFIRMED: ObservableProvider has NO debouncing - all {totalEmissions - initialCount} changes emitted!");

            subscription.Dispose();
            behaviorSubject.Dispose();
        }
    }
}