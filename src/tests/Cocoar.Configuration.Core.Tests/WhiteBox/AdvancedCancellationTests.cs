using Microsoft.Extensions.Logging.Abstractions;
using System.Reactive.Subjects;
using Cocoar.Configuration.Rules;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

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

    [Fact]
    public async Task MultipleOverlappingChanges_HandlesCancellationCorrectly()
    {

        var subject1 = new BehaviorSubject<CancellationConfig>(new("provider-1", 0, "initial"));
        var subject2 = new BehaviorSubject<CancellationConfig>(new("provider-2", 0, "initial"));
        var subject3 = new BehaviorSubject<CancellationConfig>(new("provider-3", 0, "initial"));

        TrackForDisposal(subject1);
        TrackForDisposal(subject2);
        TrackForDisposal(subject3);

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new(subject1),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new()),

            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new(subject2),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new()),

            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new(subject3),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new())
        };

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 30);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Wait for initial state
        await Task.Delay(100);


        subject3.OnNext(new("provider-3", 1, "changed"));
        await Task.Delay(10);
        subject2.OnNext(new("provider-2", 1, "changed"));
        await Task.Delay(10);
        subject1.OnNext(new("provider-1", 1, "changed"));

        // Wait for processing to complete
        await Task.Delay(200);


        var finalConfig = configManager.GetConfig<CancellationConfig>();
        Assert.NotNull(finalConfig);
        Assert.Equal("changed", finalConfig.Status);
        Assert.Equal(1, finalConfig.Value);
    }
    [Fact]
    public async Task RapidOverlappingChanges_HandlesCancellationsChaotically()
    {

        var subjects = Enumerable.Range(0, 4)
            .Select(i => new BehaviorSubject<CancellationConfig>(new($"provider-{i}", 0, "initial")))
            .ToList();

        foreach (var subject in subjects)
        {
            TrackForDisposal(subject);
        }

        var rules = subjects.Select((subject, index) => 
            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new())).ToList();

        var configManager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 100);
        TrackForDisposal(configManager);
        configManager.Initialize();

        // Wait for initial state
        await Task.Delay(150);


        // This should cause multiple cancellations as earlier indices arrive
        for (var wave = 0; wave < 3; wave++)
        {
            for (var i = subjects.Count - 1; i >= 0; i--)
            {
                subjects[i].OnNext(new($"provider-{i}", wave * 10 + i, $"wave-{wave}"));
                await Task.Delay(10); // Small delay between rapid changes
            }
        }

        // Wait for all changes to settle
        await Task.Delay(500);


        var finalConfig = configManager.GetConfig<CancellationConfig>();
        Assert.NotNull(finalConfig);

        // Configuration should reflect the final wave of changes
        Assert.True(finalConfig.Status.Contains("wave-2"), 
            $"Config should reflect final wave changes, got status: {finalConfig.Status}");
    }
    [Fact]
    public async Task HighFrequencyChanges_DebounceAndCoalesceCorrectly()
    {

        var subject = new BehaviorSubject<CancellationConfig>(new("burst", 0, "initial"));
        TrackForDisposal(subject);

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<CancellationConfig>, ObservableProviderOptions<CancellationConfig>, ObservableProviderQuery>(
                _ => new(subject),
                _ => ObservableProviderQuery.Default,
                typeof(CancellationConfig),
                new())
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


        const int changeCount = 100;
        for (var i = 1; i <= changeCount; i++)
        {
            subject.OnNext(new("burst", i, $"rapid-{i}"));
            await Task.Delay(1); // 1ms intervals - much faster than debounce
        }

        // Wait for debouncing to complete
        await Task.Delay(200);


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