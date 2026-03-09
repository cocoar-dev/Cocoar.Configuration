#define INCLUDE_MICROSOFT_ADAPTER_TESTS
using Xunit;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.MicrosoftAdapter;

namespace Cocoar.Configuration.Providers.Tests.MicrosoftAdapter;

public class MicrosoftAdapterSmokeTests
{
#if INCLUDE_MICROSOFT_ADAPTER_TESTS
    private sealed class AppConfig { public string? Value { get; set; } }

    [Fact] 
    public void Adapter_Loads_From_MemoryConfig()
    {
        var dict = new Dictionary<string,string?>
        {
            ["App:Value"] = "42"
        };
        var configSource = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = dict };

        var rule = MicrosoftConfigurationSourceProvider.CreateRule<AppConfig>(_ => new(configSource, configurationPrefix: "App"));
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));
        var config = manager.GetConfig<AppConfig>();
        Assert.Equal("42", config!.Value);
        var snap = manager.GetHealthService().Snapshot;
        Assert.Equal(Health.HealthStatus.Healthy, snap.OverallStatus);
        Assert.Equal(Health.RuleResultStatus.Up, snap.Rules[0].Status);
    }
#endif
}
