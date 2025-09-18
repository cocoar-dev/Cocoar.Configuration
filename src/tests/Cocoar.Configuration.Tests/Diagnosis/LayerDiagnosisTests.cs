using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Text.Json;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Tests.Diagnosis;

/// <summary>
/// Layer-by-layer diagnostic tests for systematic debugging of reactive configuration issues.
/// 
/// These tests isolate each architectural layer to identify where problems occur:
/// 1. BehaviorSubject - Raw reactive primitive behavior
/// 2. ReactiveConfig - Configuration wrapper behavior  
/// 3. Direct ConfigManager - Minimal orchestration
/// 4. Full ConfigManager - Complete system with debouncing
/// 
/// Use these tests as a template for diagnosing future reactive system issues.
/// They demonstrate the proper methodology for isolating problems in complex reactive architectures.
/// </summary>
public class LayerDiagnosisTests
{
    #region Layer 1: Raw Reactive Primitives

    [Fact]
    public void Layer1_BehaviorSubject_EmitsAllRapidChanges()
    {
        // Test the BehaviorSubject itself - should emit ALL values
        var subject = new BehaviorSubject<int>(0);
        var emissions = new List<int>();
        var subscription = subject.Subscribe(value => emissions.Add(value));

        // Emit rapid changes
        for (int i = 1; i <= 10; i++)
        {
            subject.OnNext(i);
        }

        // BehaviorSubject should emit ALL values immediately
        Assert.Equal(11, emissions.Count); // Initial + 10 changes
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, emissions);

        subscription.Dispose();
        subject.Dispose();
    }

    #endregion

    #region Layer 2: ReactiveConfig Wrapper

    [Fact]
    public async Task Layer2_ReactiveConfig_PreservesAllEmissions()
    {
        // Test if IReactiveConfig itself loses emissions when BehaviorSubject is updated directly
        var subject = new BehaviorSubject<TestConfig>(new TestConfig { Value = 0 });
        var reactiveConfig = new ReactiveConfig<TestConfig>(subject, NullLogger.Instance);

        var emissions = new List<TestConfig>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        // Direct updates to the underlying BehaviorSubject
        for (int i = 1; i <= 10; i++)
        {
            subject.OnNext(new TestConfig { Value = i });
        }

        // Small delay to ensure synchronous operations complete
        await Task.Delay(10);

        // Should emit ALL values if IReactiveConfig doesn't lose them
        Assert.Equal(11, emissions.Count); // Initial + 10 changes
        var values = emissions.Select(e => e.Value).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, values);

        subscription.Dispose();
        reactiveConfig.Dispose();
        subject.Dispose();
    }

    #endregion

    #region Layer 3: Minimal ConfigManager

    /// <summary>
    /// Minimal ConfigManager implementation for testing layer 3 (orchestration without full complexity)
    /// </summary>
    private class DiagnosticConfigManager : IDisposable
    {
        private readonly BehaviorSubject<TestConfig> _subject;
        private readonly ReactiveConfig<TestConfig> _reactiveConfig;

        public DiagnosticConfigManager()
        {
            _subject = new BehaviorSubject<TestConfig>(new TestConfig { Value = 0 });
            _reactiveConfig = new ReactiveConfig<TestConfig>(_subject, NullLogger.Instance);
        }

        public IReactiveConfig<TestConfig> GetReactiveConfig() => _reactiveConfig;

        public void UpdateConfig(TestConfig config)
        {
            _subject.OnNext(config);
        }

        public void Dispose()
        {
            _reactiveConfig?.Dispose();
            _subject?.Dispose();
        }
    }

    [Fact]
    public async Task Layer3_MinimalConfigManager_HandlesCascadingUpdates()
    {
        // Test if the issue is in ConfigManager orchestration vs BehaviorSubject/ReactiveConfig
        using var diagnosticManager = new DiagnosticConfigManager();
        var reactiveConfig = diagnosticManager.GetReactiveConfig();

        var emissions = new List<TestConfig>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        // Rapid updates directly to the diagnostic manager
        for (int i = 1; i <= 10; i++)
        {
            diagnosticManager.UpdateConfig(new TestConfig { Value = i });
        }

        await Task.Delay(10);

        // Should emit ALL values if ReactiveConfig works correctly
        Assert.Equal(11, emissions.Count); // Initial + 10 changes
        var values = emissions.Select(e => e.Value).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, values);

        subscription.Dispose();
    }

    #endregion

    #region Layer 4: Full System with Debouncing

    [Fact]
    public async Task Layer4_FullConfigManager_DemonstratesDebouncing()
    {
        // Test the FULL ConfigManager to see debouncing behavior
        var provider = new DiagnosticProvider();
        var rules = new ConfigRule[]
        {
            new ConfigRule(typeof(DiagnosticProvider), new EmptyConfig(), new EmptyQuery(), typeof(TestConfig))
        };

        var factory = new Func<Type, IProviderConfiguration, ConfigurationProvider>((type, config) => provider);
        var configManager = new ConfigManager(rules, null, NullLogger.Instance, factory).Initialize();

        var reactiveConfig = configManager.GetReactiveConfig<TestConfig>();
        var emissions = new List<TestConfig>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        await Task.Delay(50); // Wait for initial
        var initialCount = emissions.Count;

        // Rapid provider changes (this should trigger debouncing)
        for (int i = 1; i <= 10; i++)
        {
            provider.EmitChange(new TestConfig { Value = i });
        }

        // Wait for debouncing to settle
        await Task.Delay(500); // Longer than debounce period

        var finalCount = emissions.Count;

        // This is WHERE we expect debouncing behavior
        Console.WriteLine($"Initial emissions: {initialCount}");
        Console.WriteLine($"Final emissions: {finalCount}");
        Console.WriteLine($"Values: [{string.Join(", ", emissions.Skip(initialCount).Select(e => e.Value))}]");

        // The key question: Do we get the FINAL value (10)?
        var lastEmission = emissions.Skip(initialCount).LastOrDefault();
        Assert.NotNull(lastEmission);
        Assert.Equal(10, lastEmission.Value); // Must get the final value!

        // We might get fewer total emissions due to debouncing (that's by design)
        // But we MUST get the final value

        subscription.Dispose();
        provider.Dispose();
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Controllable provider for testing full ConfigManager behavior
    /// </summary>
    private class DiagnosticProvider : ConfigurationProvider, IDisposable
    {
        private readonly Subject<JsonElement> _changes = new();
        private TestConfig _current = new() { Value = 0 };

        public override Task<JsonElement> FetchConfigurationAsync(IProviderQuery query, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(_current);
            return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(json));
        }

        public override IObservable<JsonElement> Changes(IProviderQuery query)
        {
            return _changes.AsObservable();
        }

        public void EmitChange(TestConfig config)
        {
            _current = config;
            var json = JsonSerializer.Serialize(config);
            var element = JsonSerializer.Deserialize<JsonElement>(json);
            _changes.OnNext(element);
        }

        public void Dispose()
        {
            _changes?.Dispose();
        }
    }

    /// <summary>
    /// Simple test configuration object
    /// </summary>
    private class TestConfig
    {
        public int Value { get; set; }
    }

    /// <summary>
    /// Empty provider configuration for testing
    /// </summary>
    private class EmptyConfig : IProviderConfiguration
    {
        public string GenerateProviderKey() => "test";
    }

    /// <summary>
    /// Empty provider query for testing
    /// </summary>
    private class EmptyQuery : IProviderQuery
    {
    }

    #endregion
}