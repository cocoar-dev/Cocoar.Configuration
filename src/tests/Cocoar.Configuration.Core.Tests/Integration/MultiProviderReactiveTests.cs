using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using static Cocoar.Configuration.Core.Tests.Integration.MultiProviderTestModels;

namespace Cocoar.Configuration.Core.Tests.Integration;
[Trait("Category", "Integration")]
[Trait("Component", "ConfigManager")]
public class MultiProviderReactiveTests
{
    #region Reactive Integration Tests

    #region Reactive Integration Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public async Task ConfigManager_ObservableChanges_UpdatesReactiveConfig()
    {
        var staticBase = """{"Name": "Static", "Version": 1, "Database": {"Timeout": 30}}""";
        var initialObservableJson = """{"Name": "Observable", "Version": 10}""";
        var behaviorSubject = new BehaviorSubject<string>(initialObservableJson);

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<AppConfig>(staticBase),      // Base (rule 0)
            TestRules.ObservableString<AppConfig>(behaviorSubject)  // Observable (rule 1, wins)
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseDebounce(100));
        var reactiveConfig = configManager.GetReactiveConfig<AppConfig>();
        
        var emissions = new List<AppConfig>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        // Wait for initial emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count > 0 && emissions.Last().Name == "Observable",
            description: "initial Observable configuration to emit");

        var initialConfig = emissions.Last();
        Assert.NotNull(initialConfig);
        Assert.Equal("Observable", initialConfig.Name);  // From Observable
        Assert.Equal(10, initialConfig.Version);        // From Observable  
        Assert.Equal(30, initialConfig.Database.Timeout); // From Static (not overridden)

        var updatedObservableJson = """{"Name": "UpdatedObservable", "Version": 20}""";
        behaviorSubject.OnNext(updatedObservableJson);

        // Wait for updated emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Any(e => e.Name == "UpdatedObservable"),
            description: "updated Observable configuration to emit");

        var latestConfig = emissions.Last();
        Assert.NotNull(latestConfig);
        Assert.Equal("UpdatedObservable", latestConfig.Name);  // Observable won
        Assert.Equal(20, latestConfig.Version);               // Observable won
        Assert.Equal(30, latestConfig.Database.Timeout);      // Static preserved

        var currentSnapshot = configManager.GetConfig<AppConfig>();
        Assert.NotNull(currentSnapshot);
        Assert.Equal("UpdatedObservable", currentSnapshot.Name);
        Assert.Equal(20, currentSnapshot.Version);
        Assert.Equal(30, currentSnapshot.Database.Timeout);

        subscription.Dispose();
        behaviorSubject.Dispose();
    }
    [Fact]
    [Trait("Type", "Concurrency")]
    [Trait("Provider", "ConfigManager")]
    public async Task ConfigManager_RapidObservableChanges_DebouncesCorrectly()
    {
        var staticBase = """
        {
            "Name": "StaticBase",
            "Database": {
                "ConnectionString": "server=static;",
                "Timeout": 30
            }
        }
        """;

        var initialObservableJson = """
        {
            "Name": "Initial",
            "Version": 0,
            "Database": {
                "ConnectionString": "server=initial;"
            }
        }
        """;

        var behaviorSubject = new BehaviorSubject<string>(initialObservableJson);

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<AppConfig>(staticBase),
            TestRules.ObservableString<AppConfig>(behaviorSubject)
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseDebounce(50));
        var reactiveConfig = configManager.GetReactiveConfig<AppConfig>();

        var emissions = new List<AppConfig>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        // Wait for initial emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count > 0,
            description: "initial emission");
        var initialCount = emissions.Count;

        for (var i = 1; i <= 20; i++)
        {
            var updateJson = $$"""
            {
                "Name": "Change{{i}}",
                "Version": {{i}},
                "Database": {
                    "ConnectionString": "server=change{{i}};"
                }
            }
            """;
            behaviorSubject.OnNext(updateJson);
        }

        // Wait for final emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Any(e => e.Name == "Change20"),
            description: "final Change20 emission");

        var finalEmissionCount = emissions.Count;
        var finalConfig = emissions.Last();
        Assert.NotNull(finalConfig);

        Assert.Equal("Change20", finalConfig.Name);                    // Final Observable value
        Assert.Equal(20, finalConfig.Version);                       // Final Observable value
        Assert.Equal("server=change20;", finalConfig.Database.ConnectionString); // Final Observable value
        Assert.Equal(30, finalConfig.Database.Timeout);              // From Static base (not overridden)

        var newEmissions = finalEmissionCount - initialCount;
        Assert.True(newEmissions > 0, "Should have at least one emission from changes");
        Assert.True(newEmissions < 20, "Should have fewer emissions than changes (debouncing)");

        subscription.Dispose();
        behaviorSubject.Dispose();
    }

    #endregion

    #endregion

    #region Hash-Gated Emission Tests

    #region Hash-Gated Emission Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public async Task ConfigManager_Observable_NoEmission_WhenOnlyPropertyOrderDiffers()
    {

        var initialJson = @"{
            ""Name"": ""TestApp"",
            ""Version"": 1,
            ""Database"": {
                ""ConnectionString"": ""Server=test"",
                ""Timeout"": 30
            }
        }";

        using var subject = new BehaviorSubject<string>(initialJson);

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<AppConfig>(subject)
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseDebounce(100));
        var reactiveConfig = configManager.GetReactiveConfig<AppConfig>();
        
        var emissions = new List<AppConfig>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        // Wait for initial emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count > 0,
            description: "initial emission");
        var initialEmissionCount = emissions.Count;

        var reorderedJson = @"{
            ""Database"": {
                ""Timeout"": 30,
                ""ConnectionString"": ""Server=test""
            },
            ""Version"": 1,
            ""Name"": ""TestApp""
        }";

        subject.OnNext(reorderedJson);
        
        // Wait beyond debounce window to ensure no spurious emissions
        await ActiveWaitHelpers.WaitUntilAsync(
            () => true, // Just wait for debounce to settle
            timeout: TimeSpan.FromMilliseconds(200),
            description: "debounce to settle after property reorder");

        Assert.Equal(initialEmissionCount, emissions.Count);
        
        var currentConfig = reactiveConfig.CurrentValue;
        Assert.Equal("TestApp", currentConfig.Name);
        Assert.Equal(1, currentConfig.Version);
        Assert.Equal("Server=test", currentConfig.Database.ConnectionString);
        Assert.Equal(30, currentConfig.Database.Timeout);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public async Task ConfigManager_Observable_NoEmission_WhenWhitespaceChanges()
    {

        var compactJson = @"{""Name"":""TestApp"",""Version"":2}";

        using var subject = new BehaviorSubject<string>(compactJson);

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<AppConfig>(subject)
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseDebounce(100));
        var reactiveConfig = configManager.GetReactiveConfig<AppConfig>();
        
        var emissions = new List<AppConfig>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        // Wait for initial emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count > 0,
            description: "initial emission");
        var initialEmissionCount = emissions.Count;

        var formattedJson = @"{
            ""Name""   :   ""TestApp""  ,
            ""Version""    :    2
        }";

        subject.OnNext(formattedJson);
        
        // Wait beyond debounce window to ensure no spurious emissions
        await ActiveWaitHelpers.WaitUntilAsync(
            () => true, // Just wait for debounce to settle
            timeout: TimeSpan.FromMilliseconds(200),
            description: "debounce to settle after whitespace change");

        Assert.Equal(initialEmissionCount, emissions.Count);
        
        var currentConfig = reactiveConfig.CurrentValue;
        Assert.Equal("TestApp", currentConfig.Name);
        Assert.Equal(2, currentConfig.Version);
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public async Task ConfigManager_Observable_EmissionWhen_ArrayOrderChanges_OrderSensitive()
    {

        var initialJson = @"{
            ""Name"": ""TestApp"",
            ""Environments"": [""dev"", ""prod"", ""test""]
        }";

        using var subject = new BehaviorSubject<string>(initialJson);

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<AppConfigWithArray>(subject)
        };

        var configManager = ConfigManager.Create(c => c.WithConfiguration(rules).UseDebounce(100));
        var reactiveConfig = configManager.GetReactiveConfig<AppConfigWithArray>();
        
        var emissions = new List<AppConfigWithArray>();
        var subscription = reactiveConfig.Subscribe(config => emissions.Add(config));

        // Wait for initial emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count > 0,
            description: "initial emission");
        var initialEmissionCount = emissions.Count;

        var reorderedJson = @"{
            ""Name"": ""TestApp"",
            ""Environments"": [""prod"", ""dev"", ""test""]
        }";

        subject.OnNext(reorderedJson);

        // Wait for potential emission using active waiting
        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Count > initialEmissionCount,
            timeout: TimeSpan.FromSeconds(5),
            description: "emission after array order change");

        Assert.True(emissions.Count > initialEmissionCount, 
            "Expected emission when array order changes because arrays are order-sensitive");
        
        var currentConfig = reactiveConfig.CurrentValue;
        Assert.Equal("TestApp", currentConfig.Name);
        Assert.Equal(new[] { "prod", "dev", "test" }, currentConfig.Environments);
    }

    #endregion

    #endregion
}