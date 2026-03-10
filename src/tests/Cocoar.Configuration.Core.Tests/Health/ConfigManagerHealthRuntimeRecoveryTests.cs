using System.Text.Json;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Providers.Abstractions;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Health;

public sealed class ConfigManagerHealthRuntimeRecoveryTests
{
    private sealed class SmallConfig { public string Name { get; set; } = string.Empty; }

    [Fact]
    [Trait("Type","Unit")]  
    [Trait("Provider","StaticFailingProvider")]
    public void OptionalRule_ProviderFailure_ShowsDegraded()
    {
        // Use the proven pattern from LeanHealthIntegrationTests
        var goodRule = BuildWorkingRule(required: true);
        var failingRule = BuildAlwaysFailingRule(required: false, new InvalidOperationException("Test runtime failure"));
        
        // Initialization will attempt recompute - second optional fails -> degraded
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[] { goodRule, failingRule }));
        
        var snap = manager.GetHealthService().Snapshot;
        Assert.Equal(HealthStatus.Degraded, snap.OverallStatus);
        Assert.Equal(RuleResultStatus.Up, snap.Rules[0].Status);  // first rule up
        Assert.Equal(RuleResultStatus.Down, snap.Rules[1].Status); // second rule down
        Assert.True(snap.Rules[1].FailureCount >= 1);
        Assert.Contains("Test runtime failure", snap.Rules[1].ErrorMessage ?? "");
    }

    private static ConfigRule BuildWorkingRule(bool required)
    {
        var options = new WorkingProviderOptions("""{"Name": "Working"}""");
        var query = new WorkingProviderQuery();
        return new(typeof(WorkingProvider), options, query, typeof(SmallConfig), new(Required: required));
    }
    
    private static ConfigRule BuildAlwaysFailingRule(bool required, Exception ex)
    {
        var providerOptions = new AlwaysFailingProviderOptions(ex);
        var query = new AlwaysFailingProviderQuery();
        return new(typeof(AlwaysFailingProvider), providerOptions, query, typeof(SmallConfig), new(Required: required));
    }
}

// Simple working provider
internal sealed record WorkingProviderOptions(string Json) : IProviderConfiguration;
internal sealed record WorkingProviderQuery() : IProviderQuery;

internal sealed class WorkingProvider : ConfigurationProvider<WorkingProviderOptions, WorkingProviderQuery>
{
    public WorkingProvider(WorkingProviderOptions options) : base(options) { }
    
    public override Task<byte[]> FetchConfigurationBytesAsync(WorkingProviderQuery query, CancellationToken ct = default)
    {
        using var document = JsonDocument.Parse(ProviderOptions.Json);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document.RootElement.Clone());
        return Task.FromResult(bytes);
    }
    
    public override IObservable<byte[]> ChangesAsBytes(WorkingProviderQuery query) 
        => System.Reactive.Linq.Observable.Empty<byte[]>();
}

// Always failing provider
internal sealed record AlwaysFailingProviderOptions(Exception Ex) : IProviderConfiguration;
internal sealed record AlwaysFailingProviderQuery() : IProviderQuery;

internal sealed class AlwaysFailingProvider : ConfigurationProvider<AlwaysFailingProviderOptions, AlwaysFailingProviderQuery>
{
    public AlwaysFailingProvider(AlwaysFailingProviderOptions options) : base(options) { }
    
    public override Task<byte[]> FetchConfigurationBytesAsync(AlwaysFailingProviderQuery query, CancellationToken ct = default)
        => Task.FromException<byte[]>(ProviderOptions.Ex);
    
    public override IObservable<byte[]> ChangesAsBytes(AlwaysFailingProviderQuery query) 
        => System.Reactive.Linq.Observable.Empty<byte[]>();
}



