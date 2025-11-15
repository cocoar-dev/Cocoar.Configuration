using System.Reactive.Subjects;
using Cocoar.Configuration.Providers;

using Cocoar.Configuration.Core.Tests.Helpers;
using Cocoar.Configuration.Core.Tests.TestUtilities;

namespace Cocoar.Configuration.Core.Tests.Core;

public class ConfigManagerOrchestrationTests
{
    private readonly struct Unit
    {
        public static readonly Unit Default = new();
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManager")]
    public async Task Initialize_Does_Not_Recompute_From_Subscription_And_Recomputes_On_Change()
    {
        var initialJson = """{"Ok": true, "Count": 1}""";
        var changedJson = """{"Ok": true, "Count": 2}""";
        
        var behaviorSubject = new BehaviorSubject<string>(initialJson);
        
        var rule = TestRules.ObservableString<TestConfig>(behaviorSubject, required: true);

        using var manager = new ConfigManager(new[] { rule });
        manager.Initialize();
        
        var initialConfig = manager.GetConfig<TestConfig>();
        Assert.NotNull(initialConfig);
        Assert.Equal(1, initialConfig!.Count);

        behaviorSubject.OnNext(changedJson);

        // Wait for the configuration to update using condition-based waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => manager.GetConfig<TestConfig>()?.Count == 2,
            description: "configuration Count to update to 2");

        var updatedConfig = manager.GetConfig<TestConfig>();
        Assert.NotNull(updatedConfig);
        Assert.Equal(2, updatedConfig!.Count);
        
        behaviorSubject.OnCompleted();
        behaviorSubject.Dispose();
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManager")]
    public void StaticProvider_Configuration_LoadsCorrectly()
    {
        var rule = TestRules.StaticJson<TestConfig>("""{"Enabled": true, "Value": 42}""", required: true);

        using var manager = new ConfigManager(new[] { rule });
        manager.Initialize();
        var config = manager.GetConfig<TestConfig>();

        Assert.NotNull(config);
        Assert.True(config!.Enabled);
        Assert.Equal(42, config.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManager")]
    public void MultipleStaticRules_MergeCorrectly()
    {
        var rule1 = TestRules.StaticJson<Dictionary<string, object>>(
            """{"base": "config1", "shared": "from-first"}""",
            required: true);

        var rule2 = TestRules.StaticJson<Dictionary<string, object>>(
            """{"additional": "config2", "shared": "from-second"}""",
            required: true);

        using var manager = new ConfigManager(new[] { rule1, rule2 });
        manager.Initialize();
        var config = manager.GetConfig<Dictionary<string, object>>();

        Assert.NotNull(config);
        Assert.True(config!.ContainsKey("base"));
        Assert.True(config.ContainsKey("additional"));
        Assert.Equal("from-second", config["shared"].ToString()); // Later rule wins
    }

    public sealed class TestConfig
    {
        public bool Enabled { get; set; }
        public int Value { get; set; }
        public bool Ok { get; set; }
        public int Count { get; set; }
    }
}



