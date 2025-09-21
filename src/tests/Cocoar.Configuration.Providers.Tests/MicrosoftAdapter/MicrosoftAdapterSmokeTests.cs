#define INCLUDE_MICROSOFT_ADAPTER_TESTS
using System;
using Microsoft.Extensions.Configuration;
using Xunit;
using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.MicrosoftAdapter;

namespace Cocoar.Configuration.Providers.Tests.MicrosoftAdapter;

/// <summary>
/// MicrosoftAdapterSmokeTests
/// --------------------------
/// PURPOSE
///   Smoke tests for the MicrosoftConfigurationProvider adapter that bridges
///   Microsoft.Extensions.Configuration with Cocoar's configuration system.
///   Validates basic integration and value reading capabilities.
/// 
/// SCOPE
///   - Basic adapter functionality with in-memory configuration
///   - Value reading and type conversion through adapter
///   - Integration with Cocoar configuration binding system
///   - Adapter lifecycle and disposal behavior
/// 
/// COVERAGE
///   - Memory-based configuration source integration
///   - String value retrieval through adapter
///   - Configuration binding with adapted Microsoft providers
///   - Basic adapter instantiation and usage patterns
/// 
/// CONSTRAINTS
///   - Tests are conditionally compiled (INCLUDE_MICROSOFT_ADAPTER_TESTS)
///   - Uses Microsoft.Extensions.Configuration as data source
///   - Focuses on adapter functionality, not Microsoft provider specifics
/// </summary>
public class MicrosoftAdapterSmokeTests
{
#if INCLUDE_MICROSOFT_ADAPTER_TESTS
    private sealed class AppConfig { public string? Value { get; set; } }

    [Fact] 
    public void Adapter_Loads_From_MemoryConfig()
    {
        var dict = new System.Collections.Generic.Dictionary<string,string?>
        {
            ["App:Value"] = "42"
        };
        var configSource = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = dict };

        var rule = Rule.From.MicrosoftSource(_ => new MicrosoftConfigurationSourceRuleOptions(configSource)).Select("App").For<AppConfig>().Build();
        using var manager = new ConfigManager(new[]{rule}).Initialize();
        var config = manager.GetConfig<AppConfig>();
        Assert.Equal("42", config!.Value);
        var snap = manager.GetHealthService().Snapshot;
        Assert.Equal(Health.HealthStatus.Healthy, snap.OverallStatus);
        Assert.Equal(Health.RuleResultStatus.Up, snap.Rules[0].Status);
    }
#endif
}
