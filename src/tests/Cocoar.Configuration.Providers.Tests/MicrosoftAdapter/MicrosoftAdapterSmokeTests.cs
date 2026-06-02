#define INCLUDE_MICROSOFT_ADAPTER_TESTS
using Xunit;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.MicrosoftAdapter;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.Providers.Tests.MicrosoftAdapter;

public class MicrosoftAdapterSmokeTests
{
#if INCLUDE_MICROSOFT_ADAPTER_TESTS
    private sealed class AppConfig { public string? Value { get; set; } }

    [Fact]
    [Trait("Type", "Unit")]
    public void Adapter_Loads_From_IConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Value"] = "99"
            })
            .Build();

        using var manager = ConfigManager.Create(c => c.UseConfiguration(
            rules => [rules.For<AppConfig>().FromIConfiguration(configuration).Select("App")]));
        var config = manager.GetConfig<AppConfig>();
        Assert.Equal("99", config!.Value);
        Assert.Equal(Health.HealthStatus.Healthy, manager.HealthStatus);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Adapter_Loads_From_IConfiguration_NoSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Value"] = "direct"
            })
            .Build();

        using var manager = ConfigManager.Create(c => c.UseConfiguration(
            rules => [rules.For<AppConfig>().FromIConfiguration(configuration)]));
        var config = manager.GetConfig<AppConfig>();
        Assert.Equal("direct", config!.Value);
    }

#endif
}
