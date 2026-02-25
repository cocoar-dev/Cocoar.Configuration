using System.Text.Json;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Integration;

/// <summary>
/// Debug test to understand how configuration merging works
/// </summary>
public class ConfigMergingDebugTest
{
    public class TestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public SubConfig Sub { get; set; } = new();
    }

    public class SubConfig
    {
        public string Property { get; set; } = string.Empty;
        public int Number { get; set; }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public void Debug_HowConfigMergingWorks()
    {

        var staticBase = """
        {
            "Name": "Static",
            "Value": 100,
            "Sub": {
                "Property": "StaticProp",
                "Number": 50
            }
        }
        """;

        // Observable with only partial data (this should use JSON to test flattening)
        var observablePartialJson = """
        {
            "Name": "Observable",
            "Sub": {
                "Property": "ObservableProp"
            }
        }
        """;

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<TestConfig>(staticBase),
            TestRules.StaticJson<TestConfig>(observablePartialJson)
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules));
        var config = configManager.GetConfig<TestConfig>();

        // Expected flattened merge:
        // Rule 0: Name=Static, Value=100, Sub.Property=StaticProp, Sub.Number=50
        // Rule 1: Name=Observable, Sub.Property=ObservableProp  
        // Result: Name=Observable (overridden), Value=100 (kept), Sub.Property=ObservableProp (overridden), Sub.Number=50 (kept)

        Assert.NotNull(config);
        Assert.Equal("Observable", config.Name);         // Should be overridden
        Assert.Equal(100, config.Value);                // Should be kept from static
        Assert.Equal("ObservableProp", config.Sub.Property); // Should be overridden
        Assert.Equal(50, config.Sub.Number);            // Should be kept from static
    }
}



