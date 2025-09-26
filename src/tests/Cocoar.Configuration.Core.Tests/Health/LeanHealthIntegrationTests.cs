using System.Text.Json;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.Configuration.Core.Tests.Health;

public class LeanHealthIntegrationTests
{
    [Fact]
    public void Initial_State_ShouldBeUnknown()
    {
        var rule = BuildStaticRule(required:true);
        using var mgr = new ConfigManager(new[]{rule}, logger: NullLogger.Instance);
        var health = mgr.GetHealthService();
        Assert.Equal(HealthStatus.Unknown, health.Status);
    }

    [Fact]
    public void AfterInitialization_AllRequiredUp_ShouldBeHealthy()
    {
        var rule = BuildStaticRule(required:true);
        using var mgr = new ConfigManager(new[]{rule}, logger: NullLogger.Instance);
        mgr.Initialize();
        var snap = mgr.GetHealthService().Snapshot;
        Assert.Equal(HealthStatus.Healthy, snap.OverallStatus);
        Assert.All(snap.Rules, r => Assert.Equal(RuleResultStatus.Up, r.Status));
    }

    [Fact]
    public void OptionalFailure_ShouldYieldDegraded()
    {
        var ok = BuildStaticRule(required:true);
        var failing = BuildFailingRule(required:false, new InvalidOperationException("json parse failed"));
        using var mgr = new ConfigManager(new[]{ok, failing}, logger: NullLogger.Instance);
        // Initialization will attempt recompute - second optional fails -> degraded
        try { mgr.Initialize(); } catch { /* required rule didn't fail so ignore */ }
        var snap = mgr.GetHealthService().Snapshot;
        Assert.Equal(HealthStatus.Degraded, snap.OverallStatus);
        Assert.Equal(RuleResultStatus.Up, snap.Rules[0].Status);
        Assert.Equal(RuleResultStatus.Down, snap.Rules[1].Status);
    }

    [Fact]
    public void RequiredFailure_ShouldYieldUnhealthy_AndPreserveConfigVersion()
    {
        var ok = BuildStaticRule(required:true);
        var failing = BuildFailingRule(required:true, new TimeoutException("timeout"));
        using var mgr = new ConfigManager(new[]{ok, failing}, logger: NullLogger.Instance);
        Assert.ThrowsAny<Exception>(()=> mgr.Initialize());
        var snap = mgr.GetHealthService().Snapshot;
        Assert.Equal(HealthStatus.Unhealthy, snap.OverallStatus);
        Assert.Equal(RuleResultStatus.Up, snap.Rules[0].Status); // first succeeded then second failed abort
        Assert.Equal(RuleResultStatus.Down, snap.Rules[1].Status);
        Assert.Equal(0, snap.ConfigVersion); // failure should not advance version
    }

    [Fact]
    public void MidSequenceRequiredFailure_ShouldMarkTrailingUnknown()
    {
        // Build 5 rules where the 3rd fails required
        var r1 = BuildStaticRule(true);
        var r2 = BuildStaticRule(true);
        var failing = BuildFailingRule(true, new InvalidOperationException("boom"));
        var r4 = BuildStaticRule(true);
        var r5 = BuildStaticRule(true);
        using var mgr = new ConfigManager(new[]{r1,r2,failing,r4,r5}, logger: NullLogger.Instance);
        Assert.ThrowsAny<Exception>(()=> mgr.Initialize());
        var snap = mgr.GetHealthService().Snapshot;
        Assert.Equal(HealthStatus.Unhealthy, snap.OverallStatus);
        Assert.Equal(RuleResultStatus.Up, snap.Rules[0].Status);
        Assert.Equal(RuleResultStatus.Up, snap.Rules[1].Status);
        Assert.Equal(RuleResultStatus.Down, snap.Rules[2].Status);
        Assert.Equal(RuleResultStatus.Unknown, snap.Rules[3].Status);
        Assert.Equal(RuleResultStatus.Unknown, snap.Rules[4].Status);
    }

    [Fact]
    public void SkippedRule_ShouldNotDegradeHealth()
    {
        // First rule required and succeeds, second rule has UseWhen=false => Skipped
        var requiredRule = BuildStaticRule(true);
        var skipOptions = new SimpleStaticProviderOptions("{\"Value\":2}");
        var skipQuery = new SimpleStaticProviderQuery();
        var skippedRule = new ConfigRule(
            typeof(SimpleStaticProvider),
            skipOptions,
            skipQuery,
            typeof(object),
            new(Required: true, UseWhen: () => false) // logically required but inactive
        );
        using var mgr = new ConfigManager(new[]{requiredRule, skippedRule}, logger: NullLogger.Instance);
        mgr.Initialize();
        var snap = mgr.GetHealthService().Snapshot;
        Assert.Equal(2, snap.Rules.Count);
        Assert.Equal(HealthStatus.Healthy, snap.OverallStatus); // skipped does not degrade
        Assert.Equal(RuleResultStatus.Up, snap.Rules[0].Status);
        Assert.Equal(RuleResultStatus.Skipped, snap.Rules[1].Status);
        Assert.Equal(1, snap.ConfigVersion); // successful full recompute
    }

    private static ConfigRule BuildStaticRule(bool required)
    {
        var providerOptions = new SimpleStaticProviderOptions("{\"Value\":1}");
        var query = new SimpleStaticProviderQuery();
        return new(typeof(SimpleStaticProvider), providerOptions, query, typeof(object), new(Required: required));
    }

    private static ConfigRule BuildFailingRule(bool required, Exception ex)
    {
        var providerOptions = new FailingProviderOptions(ex);
        var query = new FailingProviderQuery();
        return new(typeof(FailingProvider), providerOptions, query, typeof(object), new(Required: required));
    }
}

internal sealed record SimpleStaticProviderOptions(string Json) : IProviderConfiguration;
internal sealed record SimpleStaticProviderQuery() : IProviderQuery;

internal sealed class SimpleStaticProvider : ConfigurationProvider<SimpleStaticProviderOptions, SimpleStaticProviderQuery>
{
    public SimpleStaticProvider(SimpleStaticProviderOptions options) : base(options){}
    public override Task<JsonElement> FetchConfigurationAsync(SimpleStaticProviderQuery query, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(ProviderOptions.Json);
        return Task.FromResult(doc.RootElement.Clone());
    }
    public override IObservable<JsonElement> Changes(SimpleStaticProviderQuery query) => System.Reactive.Linq.Observable.Empty<JsonElement>();
}

internal sealed record FailingProviderOptions(Exception Ex) : IProviderConfiguration;
internal sealed record FailingProviderQuery() : IProviderQuery;
internal sealed class FailingProvider : ConfigurationProvider<FailingProviderOptions, FailingProviderQuery>
{
    public FailingProvider(FailingProviderOptions options) : base(options){}
    public override Task<JsonElement> FetchConfigurationAsync(FailingProviderQuery query, CancellationToken ct = default)
        => Task.FromException<JsonElement>(ProviderOptions.Ex);
    public override IObservable<JsonElement> Changes(FailingProviderQuery query) => System.Reactive.Linq.Observable.Empty<JsonElement>();
}
