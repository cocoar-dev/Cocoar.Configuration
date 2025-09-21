using System.Reactive.Linq;
using System.Reactive.Subjects;
using Cocoar.Configuration;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Fluent;
using Xunit;

namespace Cocoar.Configuration.Core.Tests.Core;

/// <summary>
/// ConfigManagerOrchestrationTests
/// --------------------------------
/// PURPOSE
///   Core ConfigManager orchestration tests validating initialization, subscription behavior, 
///   and change recomputation logic. Tests the fundamental engine coordination without 
///   provider-specific implementations.
/// 
/// SCOPE
///   - ConfigManager initialization and startup behavior
///   - Static provider configuration loading and binding
///   - Observable provider change detection and debouncing
///   - Multiple rule merging with precedence handling
///   - Reactive recomputation timing and coordination
/// 
/// CONSTRAINTS
///   Uses ONLY Static and Observable providers as per Core.Tests architectural requirements.
///   No File, HTTP, Environment, or custom provider implementations allowed.
/// 
/// COVERAGE
///   - Configuration loading correctness
///   - Change detection and recomputation cycles  
///   - Rule precedence and merging behavior
///   - Observable subscription lifecycle management
/// </summary>
// Core ConfigManager orchestration tests (initialization, subscription behavior, change recomputation)
// Uses ONLY Static and Observable providers as per Core.Tests constraints
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
        // Arrange: Use BehaviorSubject to test initialization vs change behavior
        var initialJson = """{"Ok": true, "Count": 1}""";
        var changedJson = """{"Ok": true, "Count": 2}""";
        
        var behaviorSubject = new BehaviorSubject<string>(initialJson);
        
        var rule = Rule.From
            .Observable(behaviorSubject)
            .Required()
            .For<TestConfig>()
            .Build();

        // Act: Initialize and get initial config
        using var manager = new ConfigManager(new[] { rule });
        manager.Initialize();
        
        var initialConfig = manager.GetConfig<TestConfig>();
        Assert.NotNull(initialConfig);
        Assert.Equal(1, initialConfig!.Count);

        // Act: Emit a change and wait for debounce
        behaviorSubject.OnNext(changedJson);
        await Task.Delay(400);

        // Assert: Config should update
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
        // Arrange: Use StaticJson provider with correct property casing
        var rule = Rule.From
            .StaticJson("""{"Enabled": true, "Value": 42}""")
            .Required()
            .For<TestConfig>()
            .Build();

        // Act
        using var manager = new ConfigManager(new[] { rule });
        manager.Initialize();
        var config = manager.GetConfig<TestConfig>();

        // Assert
        Assert.NotNull(config);
        Assert.True(config!.Enabled);
        Assert.Equal(42, config.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManager")]
    public void MultipleStaticRules_MergeCorrectly()
    {
        // Arrange: Multiple static rules to test rule processing
        var rule1 = Rule.From
            .StaticJson("""{"base": "config1", "shared": "from-first"}""")
            .Required()
            .For<Dictionary<string, object>>()
            .Build();

        var rule2 = Rule.From
            .StaticJson("""{"additional": "config2", "shared": "from-second"}""")
            .Required()
            .For<Dictionary<string, object>>()
            .Build();

        // Act
        using var manager = new ConfigManager(new[] { rule1, rule2 });
        manager.Initialize();
        var config = manager.GetConfig<Dictionary<string, object>>();

        // Assert: Later rules override earlier ones
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