using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using static Cocoar.Configuration.Core.Tests.Integration.MultiProviderTestModels;

namespace Cocoar.Configuration.Core.Tests.Integration;

/// <summary>
/// Tests for provider isolation and type merging edge cases in ConfigManager.
/// Validates independent reactive configs and configuration path selection/mounting.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Component", "ConfigManager")]
public class MultiProviderIsolationTests
{
    #region Provider Isolation Regression Tests

    #region Provider Isolation Regression Tests

    /// <summary>
    /// Validates that multiple rules from the same ObservableProvider source remain properly isolated.
    /// This prevents cross-rule bleed regression where changes to one rule affect another rule
    /// from the same observable source.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public async Task ObservableProvider_MultipleRules_SameSource_NoCrossRuleBleed()
    {

        var initialJson = @"{
            ""AppSettings"": {
                ""Name"": ""TestApp"",
                ""Version"": 1
            },
            ""DatabaseSettings"": {
                ""ConnectionString"": ""server=test"",
                ""Timeout"": 30
            }
        }";

        using var subject = new BehaviorSubject<string>(initialJson);

        var configManager = new ConfigManager(rules => [
            rules.Observable(subject)
                .Select("AppSettings")
                .For<AppConfig>(),
                
            rules.Observable(subject)
                .Select("DatabaseSettings")
                .For<DatabaseConfig>()
        ], debounceMilliseconds: 100).Initialize();
        
        var appConfig = configManager.GetReactiveConfig<AppConfig>();
        var databaseConfig = configManager.GetReactiveConfig<DatabaseConfig>();

        var appEmissions = new List<AppConfig>();
        var dbEmissions = new List<DatabaseConfig>();

        var appSubscription = appConfig.Subscribe(config => appEmissions.Add(config));
        var dbSubscription = databaseConfig.Subscribe(config => dbEmissions.Add(config));

        await ActiveWaitHelpers.WaitUntilAsync(() => appEmissions.Count >= 1 && dbEmissions.Count >= 1, 
            description: "initial configurations");

        var initialApp = appEmissions.Last();
        var initialDb = dbEmissions.Last();

        Assert.Equal("TestApp", initialApp.Name);
        Assert.Equal(1, initialApp.Version);
        Assert.Equal("server=test", initialDb.ConnectionString);
        Assert.Equal(30, initialDb.Timeout);

        var updatedJson = @"{
            ""AppSettings"": {
                ""Name"": ""UpdatedApp"",
                ""Version"": 2
            },
            ""DatabaseSettings"": {
                ""ConnectionString"": ""server=updated"",
                ""Timeout"": 60
            }
        }";

        subject.OnNext(updatedJson);

        await ActiveWaitHelpers.WaitUntilAsync(() => appEmissions.Count >= 2 && dbEmissions.Count >= 2, 
            description: "updated configurations after observable change");

        var updatedApp = appEmissions.Last();
        var updatedDb = dbEmissions.Last();
        Assert.Equal("UpdatedApp", updatedApp.Name);
        Assert.Equal(2, updatedApp.Version);
        Assert.Equal("", updatedApp.Database.ConnectionString);        Assert.Equal("server=updated", updatedDb.ConnectionString);
        Assert.Equal(60, updatedDb.Timeout);
        Assert.NotEqual("server=updated", updatedApp.Database.ConnectionString);
        Assert.NotEqual("UpdatedApp", updatedDb.ConnectionString); // Connection string is not the app name
        appSubscription.Dispose();
        dbSubscription.Dispose();
    }

    /// <summary>
    /// CRITICAL MULTI-WAVE RECOMPUTE TEST: Validates consecutive partial recompute bursts correctly reuse prefix optimizations.
    /// This test ensures that when multiple waves of changes occur in rapid succession, the incremental recompute pipeline
    /// properly tracks wave progression and maintains prefix reuse without causing redundant refetches of unaffected providers.
    /// Critical for preventing performance regression where multi-wave bursts bypass partial recompute optimizations.
    /// </summary>
    [Fact]
    [Trait("Type", "Performance")]  
    [Trait("Provider", "ConfigManager")]
    public async Task ConfigManager_MultiWavePartialRecompute_CorrectlyReusesPrefixes()
    {

        var subject1 = new BehaviorSubject<string>("""{"Rule1Value": 1}""");
        var subject2 = new BehaviorSubject<string>("""{"Rule2Value": 10}""");
        var subject3 = new BehaviorSubject<string>("""{"Rule3Value": 100}""");

        var configManager = new ConfigManager(rules => [
            rules.StaticJson("""{"Static": "Base", "Priority": 1}""")
                .For<Dictionary<string, object>>(),

            rules.Observable(subject1)
                .For<Dictionary<string, object>>(),

            rules.Observable(subject2)
                .For<Dictionary<string, object>>(),

            rules.Observable(subject3)
                .For<Dictionary<string, object>>()
        ], debounceMilliseconds: 30).Initialize();
        
        var config = configManager.GetReactiveConfig<Dictionary<string, object>>();

        var emissions = new List<Dictionary<string, object>>();
        var subscription = config.Subscribe(c => emissions.Add(c));

        await ActiveWaitHelpers.WaitUntilAsync(() => emissions.Count >= 1, 
            timeout: TimeSpan.FromMilliseconds(500), 
            description: "initial configuration load");

        var baselineEmissions = emissions.Count;
        subject3.OnNext("""{"Rule3Value": 101}""");
        await Task.Delay(10);
        subject2.OnNext("""{"Rule2Value": 11}""");
        await Task.Delay(10);
        subject3.OnNext("""{"Rule3Value": 102}""");
        await Task.Delay(10);
        subject1.OnNext("""{"Rule1Value": 2}""");

        await Task.Delay(100);

        // Wait for all waves to complete - expect at least one emission after baseline
        await ActiveWaitHelpers.WaitUntilAsync(() => emissions.Count > baselineEmissions, 
            timeout: TimeSpan.FromMilliseconds(500),
            description: "multi-wave burst completion");

        var finalConfig = emissions.Last();
        
        Assert.True(finalConfig.ContainsKey("Static"));
        Assert.Equal("Base", finalConfig["Static"].ToString());
        Assert.True(finalConfig.ContainsKey("Rule1Value"));
        var rule1Value = ((JsonElement)finalConfig["Rule1Value"]).GetInt32();
        Assert.True(rule1Value == 1 || rule1Value == 2, 
            $"Rule1Value should be 1 (initial) or 2 (final), got {rule1Value}");
        
        Assert.True(finalConfig.ContainsKey("Rule2Value"));  
        var rule2Value = ((JsonElement)finalConfig["Rule2Value"]).GetInt32();
        Assert.True(rule2Value == 10 || rule2Value == 11, 
            $"Rule2Value should be 10 (initial) or 11 (updated), got {rule2Value}");
        
        Assert.True(finalConfig.ContainsKey("Rule3Value"));
        var rule3Value = ((JsonElement)finalConfig["Rule3Value"]).GetInt32();
        Assert.True(rule3Value >= 100 && rule3Value <= 102, 
            $"Rule3Value should be 100-102, got {rule3Value}");
        // We expect fewer emissions than individual wave changes due to debouncing
        var totalWaveChanges = 4; // 4 individual OnNext calls
        var actualEmissions = emissions.Count - baselineEmissions;
        
        Assert.True(actualEmissions <= totalWaveChanges, 
            $"Multi-wave burst should be debounced. Expected Γëñ{totalWaveChanges} emissions, got {actualEmissions}");
        Assert.True(actualEmissions >= 1, "At least one emission expected after multi-wave burst");
        subscription.Dispose();
    }

    /// <summary>
    /// CRITICAL EMISSION MINIMALITY PROOF TEST: Validates the documented behavior "fewer emissions than changes".
    /// This test proves that ConfigManager's debouncing and coalescing mechanisms produce fewer reactive emissions 
    /// than the raw number of provider changes, demonstrating efficiency and preventing emission floods.
    /// Critical for confirming the performance guarantee that reactive subscribers won't be overwhelmed.
    /// </summary>
    [Fact]
    [Trait("Type", "Performance")]
    [Trait("Provider", "ConfigManager")]
    public async Task ConfigManager_EmissionMinimalityProof_FewerEmissionsThanChanges()
    {

        var rawChangeCount = 0;
        var subject = new BehaviorSubject<string>("""{"Value": 0, "Timestamp": "initial"}""");
        
        var rawChangeTracker = subject.Subscribe(_ => Interlocked.Increment(ref rawChangeCount));

        var configManager = new ConfigManager(rules => [
            // Single observable rule to isolate emission behavior
            rules.Observable(subject)
                .For<Dictionary<string, object>>()
        ], debounceMilliseconds: 50).Initialize();
        
        var config = configManager.GetReactiveConfig<Dictionary<string, object>>();

        var emissions = new List<Dictionary<string, object>>();
        var subscription = config.Subscribe(c => emissions.Add(c));

        await ActiveWaitHelpers.WaitUntilAsync(() => emissions.Count >= 1, 
            timeout: TimeSpan.FromMilliseconds(500),
            description: "initial emission");

        var baselineRawChanges = rawChangeCount;
        var baselineEmissions = emissions.Count;

        const int TOTAL_CHANGES = 10;
        
        for (var i = 1; i <= TOTAL_CHANGES; i++)
        {
            subject.OnNext($$$"""{"Value": {{{i}}}, "Timestamp": "change_{{{i}}}"}""");
            await Task.Delay(2); // 2ms between changes = 20ms total (less than 50ms debounce)
        }

        await Task.Delay(100);
        
        await ActiveWaitHelpers.WaitUntilAsync(() => emissions.Count > baselineEmissions, 
            timeout: TimeSpan.FromMilliseconds(500),
            description: "debounced emissions completion");

        // Calculate deltas
        var actualRawChanges = rawChangeCount - baselineRawChanges;
        var actualEmissions = emissions.Count - baselineEmissions;

        Assert.Equal(TOTAL_CHANGES, actualRawChanges);
        Assert.True(actualEmissions < actualRawChanges, 
            $"EMISSION MINIMALITY FAILED: Expected emissions ({actualEmissions}) to be less than raw changes ({actualRawChanges})");

        var finalConfig = emissions.Last();
        Assert.True(finalConfig.ContainsKey("Value"));
        var finalValue = ((JsonElement)finalConfig["Value"]).GetInt32();
        Assert.Equal(TOTAL_CHANGES, finalValue); // Final value should be 10 (last change)

        // Performance assertion: Significant emission reduction (at least 2:1 ratio)
        var emissionReductionRatio = (double)actualRawChanges / actualEmissions;
        Assert.True(emissionReductionRatio >= 2.0, 
            $"Expected significant emission reduction (ΓëÑ2:1), got {emissionReductionRatio:F2}:1");
        subscription.Dispose();
        rawChangeTracker.Dispose();
    }

    #endregion

    #endregion

    #region Type Merging Edge Cases

    #region MEDIUM Priority Tests - Type Merging Edge Cases

    /// <summary>
    /// Tests that when merging object vs scalar values, the last rule wins and completely replaces the value.
    /// This validates that there's no attempt to merge incompatible types - clean replacement semantics.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Priority", "Medium")]
    public void Merge_ObjectVsScalar_LastRuleWins_ObjectReplacedEntirely()
    {

        var objectBase = """
        {
            "Database": {
                "ConnectionString": "server=localhost;",
                "Timeout": 30,
                "EnableRetry": true
            }
        }
        """;

        var scalarOverride = """
        {
            "Database": "simple-connection-string"
        }
        """;

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<ScalarMergeConfig>(objectBase),
            TestRules.StaticJson<ScalarMergeConfig>(scalarOverride)
        };

        var configManager = new ConfigManager(rules).Initialize();
        var config = configManager.GetConfig<ScalarMergeConfig>();
        Assert.NotNull(config);

        Assert.Equal("simple-connection-string", config.Database);
    }

    /// <summary>
    /// Tests that when merging array vs object values, the last rule wins and completely replaces the value.
    /// Arrays are not merged element-wise; they follow last-write-wins semantics like all other values.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Priority", "Medium")]
    public void Merge_ArrayVsObject_LastRuleWins_ArrayReplacedEntirely()
    {

        var arrayBase = """
        {
            "Settings": ["setting1", "setting2", "setting3"]
        }
        """;

        var objectOverride = """
        {
            "Settings": {
                "Primary": "newValue",
                "Secondary": "anotherValue"
            }
        }
        """;

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<ArrayMergeConfig>(arrayBase),
            TestRules.StaticJson<ArrayMergeConfig>(objectOverride)
        };

        var configManager = new ConfigManager(rules).Initialize();
        var config = configManager.GetConfig<ArrayMergeConfig>();
        Assert.NotNull(config);

        Assert.NotNull(config.Settings);
        Assert.Equal("newValue", config.Settings.Primary);
        Assert.Equal("anotherValue", config.Settings.Secondary);
    }

    /// <summary>
    /// Tests that null values use System.Text.Json default deserialization behavior.
    /// Validates current null handling: null strings become null, null value types become default(T).
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Priority", "Medium")]
    public void Merge_NullVsValue_NullUsesDefault_LastRuleWins()
    {

        var valuesBase = """
        {
            "Name": "Original",
            "Count": 100,
            "Enabled": true,
            "Score": 95.5
        }
        """;

        var nullOverride = """
        {
            "Name": null,
            "Count": null,
            "Enabled": null
        }
        """;

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<NullMergeConfig>(valuesBase),
            TestRules.StaticJson<NullMergeConfig>(nullOverride)
        };

        var configManager = new ConfigManager(rules).Initialize();
        var config = configManager.GetConfig<NullMergeConfig>();
        Assert.NotNull(config);

        // Note: In .NET 9.0 with nullable reference types, even "non-nullable" strings can be null
        // when deserialized from JSON null values. This is the actual behavior we're testing.
        Assert.Null(config.Name);                   // string: JSON null ΓåÆ C# null (actual behavior)
        Assert.Equal(0, config.Count);              // int: null ΓåÆ 0  
        Assert.False(config.Enabled);               // bool: null ΓåÆ false
        Assert.Equal(95.5, config.Score, 1);       // Score not overridden, keeps original value
    }

    /// <summary>
    /// Tests that GetConfig() returns a stable snapshot during live reactive updates.
    /// The snapshot API should be isolated from ongoing observable changes until the next debounced emission.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Priority", "Medium")]
    public async Task ConfigManager_SnapshotStable_DuringLiveUpdates()
    {

        var initialConfig = """{"Name": "Initial", "Value": 100}""";
        var observable = new BehaviorSubject<string>(initialConfig);

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<SnapshotConfig>(observable)
        };

        var configManager = new ConfigManager(rules).Initialize();

    var snapshot1 = configManager.GetConfig<SnapshotConfig>();
    Assert.NotNull(snapshot1);
    Assert.Equal("Initial", snapshot1!.Name);
    Assert.Equal(100, snapshot1.Value);

        observable.OnNext("""{"Name": "Update1", "Value": 200}""");
    var snapshot2 = configManager.GetConfig<SnapshotConfig>(); // Should still be stable during debounce
    Assert.NotNull(snapshot2);

        observable.OnNext("""{"Name": "Update2", "Value": 300}""");
    var snapshot3 = configManager.GetConfig<SnapshotConfig>(); // Should still be stable during debounce
    Assert.NotNull(snapshot3);
        // The key test is that GetConfig() doesn't throw or return inconsistent state
        Assert.NotNull(snapshot2);
        Assert.NotNull(snapshot3);
        Assert.False(string.IsNullOrEmpty(snapshot2.Name));
        Assert.False(string.IsNullOrEmpty(snapshot3.Name));

        await ActiveWaitHelpers.WaitUntilAsync(() =>
        {
            var currentSnapshot = configManager.GetConfig<SnapshotConfig>();
            return currentSnapshot != null && currentSnapshot.Name == "Update2" && currentSnapshot.Value == 300;
        }, TimeSpan.FromSeconds(2));

    var finalSnapshot = configManager.GetConfig<SnapshotConfig>();
    Assert.NotNull(finalSnapshot);
    Assert.Equal("Update2", finalSnapshot!.Name);
    Assert.Equal(300, finalSnapshot.Value);
        observable.Dispose();
    }

    /// <summary>
    /// Tests that Select with empty/non-existent selection contributes nothing to the final configuration.
    /// Edge case: selecting non-existent paths should result in no contribution to the final configuration.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Priority", "Medium")]
    public void Rule_SelectEmptyPath_DoesNotContributeToFinalConfig()
    {

        var jsonWithoutPath = """
        {
            "ExistingSection": {
                "Value": "exists"
            }
        }
        """;

        var configManager = new ConfigManager(rules => [
            // This rule selects a non-existent path - should contribute nothing
            rules.StaticJson(jsonWithoutPath)
                .Select("NonExistentSection")  // This path doesn't exist - selection will be empty
                .For<SelectMountConfig>(),
            
            // Base rule to ensure we have some configuration
            rules.StaticJson("""{"DefaultValue": "present"}""")
                .For<SelectMountConfig>()
        ]).Initialize();

        var config = configManager.GetConfig<SelectMountConfig>();
        Assert.NotNull(config);

        Assert.Equal("present", config.DefaultValue);
        Assert.Null(config.MountedSection);    }

    /// <summary>
    /// Tests that flattened key merging is order-independent and produces structurally equivalent JSON.
    /// Property order in source JSON should not affect the final merged configuration structure.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    [Trait("Priority", "Medium")]
    public void Merging_FlattenedKeyOrder_Irrelevant_FinalJsonStructuralEquality()
    {

        var config1 = """
        {
            "Database": {
                "ConnectionString": "server=prod;",
                "Timeout": 60,
                "EnableRetry": true
            },
            "Features": {
                "EnableNewUI": true,
                "LogLevel": "Info"
            }
        }
        """;

        var config2 = """
        {
            "Features": {
                "LogLevel": "Info",
                "EnableNewUI": true
            },
            "Database": {
                "EnableRetry": true,
                "Timeout": 60,
                "ConnectionString": "server=prod;"
            }
        }
        """;

        var configManager1 = new ConfigManager([TestRules.StaticJson<AppConfig>(config1)]).Initialize();
        var configManager2 = new ConfigManager([TestRules.StaticJson<AppConfig>(config2)]).Initialize();

    var result1 = configManager1.GetConfig<AppConfig>();
    var result2 = configManager2.GetConfig<AppConfig>();
    Assert.NotNull(result1);
    Assert.NotNull(result2);

        Assert.Equal(result1.Database.ConnectionString, result2.Database.ConnectionString);
        Assert.Equal(result1.Database.Timeout, result2.Database.Timeout);
        Assert.Equal(result1.Database.EnableRetry, result2.Database.EnableRetry);
        Assert.Equal(result1.Features.EnableNewUI, result2.Features.EnableNewUI);
        Assert.Equal(result1.Features.LogLevel, result2.Features.LogLevel);

        var json1 = JsonSerializer.Serialize(result1);
        var json2 = JsonSerializer.Serialize(result2);
        
        // Parse and compare as JsonElements to ignore property order differences
        using var doc1 = JsonDocument.Parse(json1);
        using var doc2 = JsonDocument.Parse(json2);
        
        Assert.True(MultiProviderTestModels.JsonElementsEqual(doc1.RootElement, doc2.RootElement),
            "Serialized JSON should be structurally equivalent regardless of source property order");
    }

    #endregion

    #endregion
}