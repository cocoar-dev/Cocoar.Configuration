using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.Configuration.Core.Tests.Health;

/// <summary>
/// Tests that a cancelled in-flight recompute does NOT emit a health update, per documented behaviour
/// (see docs/HEALTH.md "Cancelled Recompute" section).
/// </summary>
public class ConfigManagerHealthCancellationTests
{
    [Fact]
    public async Task ConfigManager_CancelledRecompute_DoesNotEmitHealthUpdate()
    {
        var rule = new ConfigRule(
            providerType: typeof(SlowCancellableProvider),
            providerOptions: new SlowCancellableProviderOptions(250),
            queryOptions: new SlowCancellableProviderQueryOptions(),
            concreteType: typeof(object),
            options: new(Required: true)
        );

        using var configManager = new ConfigManager([rule], logger: NullLogger.Instance);
        configManager.Initialize();

        var healthService = configManager.GetHealthService();
        var healthEmissions = new List<ConfigHealthSnapshot>();
        using var sub = healthService.SnapshotStream.Subscribe(h => healthEmissions.Add(h));
        var baselineCount = healthEmissions.Count;

        var provider = SlowCancellableProvider.LastCreatedInstance!; // Created during InitializeAsync

        provider.TriggerChange(new { Seq = 1 });
        await Task.Delay(40); // allow first fetch to start
        provider.TriggerChange(new { Seq = 2 });

        // Wait long enough for the second fetch to complete (first should have been cancelled)
        await Task.Delay(400);

        var delta = healthEmissions.Count - baselineCount;
        Assert.InRange(delta, 0, 1);

        var final = healthService.Snapshot;
        Assert.Equal(HealthStatus.Healthy, final.OverallStatus);
        Assert.All(final.Rules, r => Assert.Equal(RuleResultStatus.Up, r.Status));
    }
}

/// <summary>
/// Provider implementing the current abstract provider surface (FetchConfigurationAsync + Changes(query)).
/// It simulates cancellable work by delaying; rapid successive changes cause earlier tasks to be cancelled by
/// ConfigManager (which supplies CancellationToken).
/// </summary>
internal sealed class SlowCancellableProvider : ConfigurationProvider<SlowCancellableProviderOptions, SlowCancellableProviderQueryOptions>
{
    private readonly Subject<JsonElement> _rawChanges = new();
    private readonly int _delayMs;

    public static SlowCancellableProvider? LastCreatedInstance { get; private set; }

    public SlowCancellableProvider(SlowCancellableProviderOptions options) : base(options)
    {
        _delayMs = options.DelayMs;
        LastCreatedInstance = this;
    }

    public override async Task<JsonElement> FetchConfigurationAsync(SlowCancellableProviderQueryOptions query, CancellationToken ct = default)
    {
        // Simulate slow work
        await Task.Delay(_delayMs, ct);
        return JsonDocument.Parse("{\"Result\":\"OK\"}").RootElement.Clone();
    }

    public override IObservable<JsonElement> Changes(SlowCancellableProviderQueryOptions query)
    {
        // Project raw change payloads into dummy JsonElements (changes themselves are irrelevant; just triggers recompute)
        return _rawChanges.AsObservable();
    }

    public void TriggerChange(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        _rawChanges.OnNext(doc.RootElement.Clone());
    }
}

internal sealed record SlowCancellableProviderOptions(int DelayMs = 200) : IProviderConfiguration;
internal sealed record SlowCancellableProviderQueryOptions() : IProviderQuery;
