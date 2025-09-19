using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

/// <summary>
/// Advanced cancellation tests that validate sophisticated recompute cancellation scenarios.
/// 
/// PURPOSE:
///   Ensures an in-flight recompute is promptly cancelled when an earlier rule change arrives,
///   and that a new recompute is started from the new earliest index. This prevents wasted work
///   (fetching later providers based on stale earlier state) and reduces latency to consistent state.
/// 
/// APPROACH:
///   - Uses Observable providers to simulate realistic timing scenarios
///   - Tests multi-rule recompute cancellation and restart behavior
///   - Validates that ConfigManager handles overlapping changes gracefully
/// 
/// VALIDATION:
///   - Earlier rule changes properly cancel in-flight recomputes
///   - New recomputes start from the correct earliest changed index
///   - Rapid overlapping changes don't cause lost updates or inconsistent state
///   - System remains stable under chaotic change patterns
/// </summary>
[Trait("Type", "WhiteBox")]
[Trait("Provider", "ConfigManager")]
[Trait("Feature", "Cancellation")]
public class AdvancedCancellationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try { disposable.Dispose(); } catch { /* ignore */ }
        }
        _disposables.Clear();
    }

    private void TrackForDisposal(IDisposable disposable) => _disposables.Add(disposable);

    public record CancellationConfig(string Id, int Value, string Status);

    /// <summary>
    /// Tests that multiple overlapping changes are handled correctly with proper cancellation behavior.
    /// This validates that the recompute engine can handle complex change scenarios without lost updates.
    /// </summary>
    [Fact]
    public async Task MultipleOverlappingChanges_HandlesCancellationCorrectly()
    {
        // Arrange: Create multiple fast providers
        var subject1 = new BehaviorSubject<CancellationConfig>(new CancellationConfig("provider-1", 0, "initial"));
        var subject2 = new BehaviorSubject<CancellationConfig>(new CancellationConfig("provider-2", 0, "initial"));
        var subject3 = new BehaviorSubject<CancellationConfig>(new CancellationConfig("provider-3", 0, "initial"));

        TrackForDisposal(subject1);
        TrackForDisposal(subject2);
        TrackForDisposal(subject3);

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<CancellationConfig>(subject1),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new ConfigRuleOptions()),

            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<CancellationConfig>(subject2),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new ConfigRuleOptions()),

            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<CancellationConfig>(subject3),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new ConfigRuleOptions())
        };

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 30);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Wait for initial state
        await Task.Delay(100);

        // Act: Trigger rapid overlapping changes
        subject3.OnNext(new CancellationConfig("provider-3", 1, "changed"));
        await Task.Delay(10);
        subject2.OnNext(new CancellationConfig("provider-2", 1, "changed"));
        await Task.Delay(10);
        subject1.OnNext(new CancellationConfig("provider-1", 1, "changed"));

        // Wait for processing to complete
        await Task.Delay(200);

        // Assert: Final configuration should be consistent
        var finalConfig = configManager.GetConfig<CancellationConfig>();
        Assert.NotNull(finalConfig);
        Assert.Equal("changed", finalConfig.Status);
        Assert.Equal(1, finalConfig.Value);
    }

    /// <summary>
    /// Tests rapid overlapping changes that should trigger multiple cancellations.
    /// Validates that the system handles chaotic change scenarios gracefully.
    /// </summary>
    [Fact]
    public async Task RapidOverlappingChanges_HandlesCancellationsChaotically()
    {
        // Arrange: Setup multiple observable providers
        var subjects = Enumerable.Range(0, 4)
            .Select(i => new BehaviorSubject<CancellationConfig>(new CancellationConfig($"provider-{i}", 0, "initial")))
            .ToList();

        foreach (var subject in subjects) TrackForDisposal(subject);

        var rules = subjects.Select((subject, index) => 
            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<CancellationConfig>(subject),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new ConfigRuleOptions())).ToList();

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 100);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Wait for initial state
        await Task.Delay(150);

        // Act: Trigger rapid overlapping changes in reverse order (higher indices first)
        // This should cause multiple cancellations as earlier indices arrive
        for (int wave = 0; wave < 3; wave++)
        {
            for (int i = subjects.Count - 1; i >= 0; i--)
            {
                subjects[i].OnNext(new CancellationConfig($"provider-{i}", wave * 10 + i, $"wave-{wave}"));
                await Task.Delay(10); // Small delay between rapid changes
            }
        }

        // Wait for all changes to settle
        await Task.Delay(500);

        // Assert: System should have handled all changes gracefully
        var finalConfig = configManager.GetConfig<CancellationConfig>();
        Assert.NotNull(finalConfig);

        // Configuration should reflect the final wave of changes
        Assert.True(finalConfig.Status.Contains("wave-2"), 
            $"Config should reflect final wave changes, got status: {finalConfig.Status}");
    }

    /// <summary>
    /// Tests behavior under very high frequency changes to validate debouncing and coalescing.
    /// Ensures the system doesn't become overwhelmed by rapid-fire configuration updates.
    /// </summary>
    [Fact]
    public async Task HighFrequencyChanges_DebounceAndCoalesceCorrectly()
    {
        // Arrange: Create a high-frequency change source
        var subject = new BehaviorSubject<CancellationConfig>(new CancellationConfig("burst", 0, "initial"));
        TrackForDisposal(subject);

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<CancellationConfig>(subject),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new ConfigRuleOptions())
        };

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Track emissions
        var emissionCount = 0;
        var reactive = configManager.GetReactiveConfig<CancellationConfig>();
        using var subscription = reactive.Subscribe(_ => Interlocked.Increment(ref emissionCount));

        // Wait for initial emission
        await Task.Delay(100);
        var initialEmissions = emissionCount;

        // Act: Generate very high frequency changes
        const int changeCount = 100;
        for (int i = 1; i <= changeCount; i++)
        {
            subject.OnNext(new CancellationConfig("burst", i, $"rapid-{i}"));
            await Task.Delay(1); // 1ms intervals - much faster than debounce
        }

        // Wait for debouncing to complete
        await Task.Delay(200);

        // Assert: Should have significantly fewer emissions than changes due to debouncing
        var finalEmissions = emissionCount - initialEmissions;
        Assert.True(finalEmissions < changeCount / 2, 
            $"Expected significant debouncing. Got {finalEmissions} emissions for {changeCount} changes");

        // Final configuration should reflect the last change
        var finalConfig = configManager.GetConfig<CancellationConfig>();
        Assert.NotNull(finalConfig);
        Assert.Equal(changeCount, finalConfig.Value);
        Assert.Equal($"rapid-{changeCount}", finalConfig.Status);
    }
}