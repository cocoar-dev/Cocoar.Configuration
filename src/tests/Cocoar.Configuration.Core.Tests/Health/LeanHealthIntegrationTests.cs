using System.Text.Json;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Health;

public class LeanHealthIntegrationTests
{
    [Fact]
    public void AfterCreate_ShouldBeHealthy()
    {
        var rule = BuildStaticRule(required:true);
        using var mgr = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}).UseLogger(NullLogger.Instance));
        var health = mgr.GetHealthService();
        Assert.Equal(HealthStatus.Healthy, health.Status);
    }

    [Fact]
    public void AfterInitialization_AllRequiredUp_ShouldBeHealthy()
    {
        var rule = BuildStaticRule(required:true);
        using var mgr = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}).UseLogger(NullLogger.Instance));
        var snap = mgr.GetHealthService().Snapshot;
        Assert.Equal(HealthStatus.Healthy, snap.OverallStatus);
        Assert.All(snap.Rules, r => Assert.Equal(RuleResultStatus.Up, r.Status));
    }

    [Fact]
    public void OptionalFailure_ShouldYieldDegraded()
    {
        var ok = BuildStaticRule(required:true);
        var failing = BuildFailingRule(required:false, new InvalidOperationException("json parse failed"));
        // Initialization will attempt recompute - second optional fails -> degraded
        using var mgr = ConfigManager.Create(c => c.UseConfiguration(new[]{ok, failing}).UseLogger(NullLogger.Instance));
        var snap = mgr.GetHealthService().Snapshot;
        Assert.Equal(HealthStatus.Degraded, snap.OverallStatus);
        Assert.Equal(RuleResultStatus.Up, snap.Rules[0].Status);
        Assert.Equal(RuleResultStatus.Down, snap.Rules[1].Status);
    }

    [Fact]
    public void RequiredFailure_ShouldThrowAndYieldUnhealthy()
    {
        var ok = BuildStaticRule(required:true);
        var failing = BuildFailingRule(required:true, new TimeoutException("timeout"));
        Assert.ThrowsAny<Exception>(() =>
            ConfigManager.Create(c => c.UseConfiguration(new[]{ok, failing}).UseLogger(NullLogger.Instance)));
    }

    [Fact]
    public void MidSequenceRequiredFailure_ShouldThrow()
    {
        // Build 5 rules where the 3rd fails required
        var r1 = BuildStaticRule(true);
        var r2 = BuildStaticRule(true);
        var failing = BuildFailingRule(true, new InvalidOperationException("boom"));
        var r4 = BuildStaticRule(true);
        var r5 = BuildStaticRule(true);
        Assert.ThrowsAny<Exception>(() =>
            ConfigManager.Create(c => c.UseConfiguration(new[]{r1,r2,failing,r4,r5}).UseLogger(NullLogger.Instance)));
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
            new(Required: true, UseWhen: _ => false) // logically required but inactive
        );
        using var mgr = ConfigManager.Create(c => c.UseConfiguration(new[]{requiredRule, skippedRule}).UseLogger(NullLogger.Instance));
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
    public override Task<byte[]> FetchConfigurationBytesAsync(SimpleStaticProviderQuery query, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(ProviderOptions.Json);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc.RootElement.Clone());
        return Task.FromResult(bytes);
    }
    public override IObservable<byte[]> ChangesAsBytes(SimpleStaticProviderQuery query) => System.Reactive.Linq.Observable.Empty<byte[]>();
}

internal sealed record FailingProviderOptions(Exception Ex) : IProviderConfiguration;
internal sealed record FailingProviderQuery() : IProviderQuery;
internal sealed class FailingProvider : ConfigurationProvider<FailingProviderOptions, FailingProviderQuery>
{
    public FailingProvider(FailingProviderOptions options) : base(options){}
    public override Task<byte[]> FetchConfigurationBytesAsync(FailingProviderQuery query, CancellationToken ct = default)
        => Task.FromException<byte[]>(ProviderOptions.Ex);
    public override IObservable<byte[]> ChangesAsBytes(FailingProviderQuery query) => System.Reactive.Linq.Observable.Empty<byte[]>();
}



