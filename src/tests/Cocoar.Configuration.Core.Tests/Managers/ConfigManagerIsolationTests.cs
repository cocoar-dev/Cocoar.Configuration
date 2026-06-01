using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Managers;

/// <summary>
/// Bulletproof isolation tests for ConfigManager.
/// Tests the core orchestration, debounce logic, and recompute mechanisms using our proven bulletproof providers.
/// These tests validate that ConfigManager can reliably handle complex scenarios with multiple rapid changes.
/// </summary>
[Trait("Provider", "ConfigManager")]
public class ConfigManagerIsolationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignore disposal errors in tests
            }
        }
        _disposables.Clear();
    }

    private void TrackForDisposal(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    // Helper to create static rules using fluent API
    private static ConfigRule CreateStaticRule<T>(T config) where T : class
    {
        var rulesBuilder = new RulesBuilder();
        return rulesBuilder.For<T>().FromStaticJson(JsonSerializer.Serialize(config)).Required();
    }

    // Test configuration classes
    public class DatabaseConfig
    {
        public string ConnectionString { get; set; } = "";
        public int Timeout { get; set; }
        public bool EnableRetry { get; set; }
    }

    public class ApiConfig
    {
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public int MaxRetries { get; set; }
    }

    #region Basic Functionality Tests

    [Fact]
    [Trait("Type", "Unit")]
    public void ConfigManager_Create_ShouldReturnInitializedManager()
    {
        var testConfig = new DatabaseConfig { ConnectionString = "test", Timeout = 30 };
        var rules = new List<ConfigRule>
        {
            CreateStaticRule(testConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        // Verify initialization by checking if configs are accessible
        var config = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(config);
        Assert.Equal("test", config.ConnectionString);
        Assert.Equal(30, config.Timeout);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ConfigManager_Create_CanBeCalledMultipleTimes_IndependentInstances()
    {
        var testConfig = new DatabaseConfig { ConnectionString = "test", Timeout = 30 };
        var rules = new List<ConfigRule>
        {
            CreateStaticRule(testConfig)
        };

        var configManager1 = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager1);

        var configManager2 = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager2);

        Assert.NotSame(configManager1, configManager2);

        // Both should have accessible config
        var config1 = configManager1.GetConfig<DatabaseConfig>();
        var config2 = configManager2.GetConfig<DatabaseConfig>();
        Assert.NotNull(config1);
        Assert.NotNull(config2);
        Assert.Equal("test", config1.ConnectionString);
        Assert.Equal("test", config2.ConnectionString);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void GetConfig_WithValidConfiguration_ShouldReturnTypedObject()
    {
        var expectedConfig = new DatabaseConfig 
        { 
            ConnectionString = "Server=test;Database=app", 
            Timeout = 60,
            EnableRetry = true
        };

        var rules = new List<ConfigRule>
        {
            CreateStaticRule(expectedConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        var result = configManager.GetConfig<DatabaseConfig>();

        Assert.NotNull(result);
        Assert.Equal(expectedConfig.ConnectionString, result.ConnectionString);
        Assert.Equal(expectedConfig.Timeout, result.Timeout);
        Assert.Equal(expectedConfig.EnableRetry, result.EnableRetry);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void GetConfig_WithNonExistentType_ShouldThrow()
    {
        // With Master Backplane architecture, GetConfig throws when no rule is registered
        var testConfig = new DatabaseConfig { ConnectionString = "test" };
        var rules = new List<ConfigRule>
        {
            CreateStaticRule(testConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            configManager.GetConfig<ApiConfig>()); // Different type not configured

        Assert.Contains("ApiConfig", exception.Message);
        Assert.Contains("No configuration rule is registered", exception.Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void TryGetConfig_WithValidConfiguration_ShouldReturnTrueAndValue()
    {
        var expectedConfig = new DatabaseConfig { ConnectionString = "test", Timeout = 45 };
        var rules = new List<ConfigRule>
        {
            CreateStaticRule(expectedConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        var success = configManager.TryGetConfig<DatabaseConfig>(out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(expectedConfig.ConnectionString, result.ConnectionString);
        Assert.Equal(expectedConfig.Timeout, result.Timeout);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void TryGetConfig_WithNonExistentType_ShouldReturnFalseAndNull()
    {
        var testConfig = new DatabaseConfig();
        var rules = new List<ConfigRule>
        {
            CreateStaticRule(testConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        var success = configManager.TryGetConfig<ApiConfig>(out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void GetRequiredConfig_WithValidConfiguration_ShouldReturnValue()
    {
        var expectedConfig = new DatabaseConfig { ConnectionString = "required-test", Timeout = 90 };
        var rules = new List<ConfigRule>
        {
            CreateStaticRule(expectedConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        var result = configManager.GetConfig<DatabaseConfig>()!;

        Assert.NotNull(result);
        Assert.Equal(expectedConfig.ConnectionString, result.ConnectionString);
        Assert.Equal(expectedConfig.Timeout, result.Timeout);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void GetRequiredConfig_WithNonExistentType_ShouldThrowInvalidOperationException()
    {
        // GetRequiredConfig now delegates to GetConfig, which throws when no rule exists
        var testConfig = new DatabaseConfig();
        var rules = new List<ConfigRule>
        {
            CreateStaticRule(testConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

#pragma warning disable CS0618 // Type or member is obsolete
        var exception = Assert.Throws<InvalidOperationException>(() =>
            configManager.GetConfig<ApiConfig>()!);
#pragma warning restore CS0618

        Assert.Contains("ApiConfig", exception.Message);
        Assert.Contains("No configuration rule is registered", exception.Message);
    }

    #endregion

    #region Multiple Rules and Priority Tests

    [Fact]
    [Trait("Type", "Unit")]
    public void ConfigManager_WithMultipleRules_ShouldRespectRuleOrder()
    {
        var rules = new List<ConfigRule>
        {
            // First rule - lower priority
            CreateStaticRule(new DatabaseConfig 
            { 
                ConnectionString = "first-rule", 
                Timeout = 30 
            }),
            // Second rule - higher priority (should win)
            CreateStaticRule(new DatabaseConfig 
            { 
                ConnectionString = "second-rule", 
                Timeout = 60 
            })
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        var config = configManager.GetConfig<DatabaseConfig>();

        Assert.NotNull(config);
        Assert.Equal("second-rule", config.ConnectionString); // Second rule should win
        Assert.Equal(60, config.Timeout);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ConfigManager_WithMultipleConfigTypes_ShouldHandleBothCorrectly()
    {
        var dbConfig = new DatabaseConfig { ConnectionString = "db-connection", Timeout = 45 };
        var apiConfig = new ApiConfig { BaseUrl = "https://api.test", ApiKey = "secret", MaxRetries = 3 };

        var rules = new List<ConfigRule>
        {
            CreateStaticRule(dbConfig),
            CreateStaticRule(apiConfig)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance));
        TrackForDisposal(configManager);

        var dbResult = configManager.GetConfig<DatabaseConfig>();
        var apiResult = configManager.GetConfig<ApiConfig>();

        Assert.NotNull(dbResult);
        Assert.Equal(dbConfig.ConnectionString, dbResult.ConnectionString);
        Assert.Equal(dbConfig.Timeout, dbResult.Timeout);

        Assert.NotNull(apiResult);
        Assert.Equal(apiConfig.BaseUrl, apiResult.BaseUrl);
        Assert.Equal(apiConfig.ApiKey, apiResult.ApiKey);
        Assert.Equal(apiConfig.MaxRetries, apiResult.MaxRetries);
    }

    #endregion

    #region Debounce and Recompute Logic Tests

    [Fact]
    [Trait("Type", "Unit")]
    public async Task ConfigManager_WithObservableProvider_ShouldHandleRecomputation()
    {

        var initialConfig = new DatabaseConfig { ConnectionString = "initial", Timeout = 30 };
        var updatedConfig = new DatabaseConfig { ConnectionString = "updated", Timeout = 60 };

        var observable = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(initialConfig);
        var rules = new List<ConfigRule>
        {
            // Create rule using ObservableProvider with the actual config type
            ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new())
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(50));
        TrackForDisposal(configManager);
        TrackForDisposal(observable);

        // Verify initial state
        var initialResult = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(initialResult);
        Assert.Equal("initial", initialResult.ConnectionString);


        observable.OnNext(updatedConfig);

        // Wait for debounce and recomputation using active wait pattern
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<DatabaseConfig>();
                return config?.ConnectionString == "updated";
            },
            timeout: TimeSpan.FromSeconds(2));


        var finalResult = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(finalResult);
        Assert.Equal("updated", finalResult.ConnectionString);
        Assert.Equal(60, finalResult.Timeout);
    }

    [Fact]
    [Trait("Type", "Performance")]
    public async Task ConfigManager_DebounceLogic_ShouldCoalesceRapidChanges()
    {

        var configs = new[]
        {
            new DatabaseConfig { ConnectionString = "change1", Timeout = 10 },
            new DatabaseConfig { ConnectionString = "change2", Timeout = 20 },
            new DatabaseConfig { ConnectionString = "change3", Timeout = 30 },
            new DatabaseConfig { ConnectionString = "final", Timeout = 40 }
        };

        var observable = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(configs[0]);
        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new())
        };

        // Use longer debounce to test coalescing
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(100));
        TrackForDisposal(configManager);
        TrackForDisposal(observable);


        for (var i = 1; i < configs.Length; i++)
        {
            observable.OnNext(configs[i]);
            await Task.Delay(10); // Small delay but less than debounce period
        }

        // Wait for final recomputation using active wait
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<DatabaseConfig>();
                return config?.ConnectionString == "final";
            },
            timeout: TimeSpan.FromSeconds(2));


        var result = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(result);
        Assert.Equal("final", result.ConnectionString);
        Assert.Equal(40, result.Timeout);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public async Task ConfigManager_CancellationLogic_ShouldCancelPreviousRecompute()
    {

        var initialConfig = new DatabaseConfig { ConnectionString = "initial", Timeout = 10 };
        var observable = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(initialConfig);

        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new())
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(200));
        TrackForDisposal(configManager);
        TrackForDisposal(observable);


        observable.OnNext(new() { ConnectionString = "change1", Timeout = 20 });
        
        // Immediately trigger another change (should cancel the first recompute)
        observable.OnNext(new() { ConnectionString = "change2", Timeout = 30 });

        // Wait for final result using active wait
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<DatabaseConfig>();
                return config?.ConnectionString == "change2";
            },
            timeout: TimeSpan.FromSeconds(2));
        

        var result = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(result);
        Assert.Equal("change2", result.ConnectionString);
        Assert.Equal(30, result.Timeout);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public async Task ConfigManager_MultipleRulesRecompute_ShouldRespectRuleOrder()
    {

        var rule1Config = new DatabaseConfig { ConnectionString = "rule1-initial", Timeout = 10 };
        var rule2Config = new DatabaseConfig { ConnectionString = "rule2-initial", Timeout = 20 };
        
        var observable1 = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(rule1Config);
        var observable2 = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(rule2Config);

        var rules = new List<ConfigRule>
        {
            // First rule - lower priority
            ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable1),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new()),
            // Second rule - higher priority (should win)
            ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable2),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new())
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(50));
        TrackForDisposal(configManager);
        TrackForDisposal(observable1);
        TrackForDisposal(observable2);

        // Initial state - rule2 should win (based on "last wins" rule order)
        var initialResult = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(initialResult);
        Assert.Equal("rule2-initial", initialResult.ConnectionString);


        observable1.OnNext(new() { ConnectionString = "rule1-updated", Timeout = 15 });
        
        // Allow time for recomputation
        await Task.Delay(50);

        var afterRule1Update = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(afterRule1Update);
        Assert.Equal("rule2-initial", afterRule1Update.ConnectionString); // rule2 still wins


        observable2.OnNext(new() { ConnectionString = "rule2-updated", Timeout = 25 });
        
        // Wait for rule2 update to complete
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<DatabaseConfig>();
                return config?.ConnectionString == "rule2-updated";
            },
            timeout: TimeSpan.FromSeconds(2));


        var finalResult = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(finalResult);
        Assert.Equal("rule2-updated", finalResult.ConnectionString);
        Assert.Equal(25, finalResult.Timeout);
    }

    #endregion

    #region Stress Tests for Debounce/Cancel Logic

    [Fact]
    [Trait("Type", "Stress")]
    public async Task ConfigManager_MassiveConcurrentRecomputes_ShouldHandleDebounceCorrectly()
    {

        var observable = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(
            new() { ConnectionString = "initial", Timeout = 0 });
        
        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new())
        };

        // Short debounce to test rapid cancellation
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(30));
        TrackForDisposal(configManager);
        TrackForDisposal(observable);


        const int totalChanges = 100; // 100 rapid changes to stress-test debounce/cancel
        var changeTask = Task.Run(async () =>
        {
            for (var i = 1; i <= totalChanges; i++)
            {
                observable.OnNext(new()
                { 
                    ConnectionString = $"change-{i}", 
                    Timeout = i 
                });
                await Task.Delay(2); // Very rapid - 2ms between changes
            }
        });

        await changeTask;

        // Wait for debouncing to complete
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<DatabaseConfig>();
                return config?.ConnectionString == $"change-{totalChanges}";
            },
            timeout: TimeSpan.FromSeconds(3));


        var finalConfig = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(finalConfig);
        Assert.Equal($"change-{totalChanges}", finalConfig.ConnectionString);
        Assert.Equal(totalChanges, finalConfig.Timeout);
    }

    [Fact]
    [Trait("Type", "Stress")]
    public async Task ConfigManager_RapidDebounceStorm_ShouldCoalesceCorrectly()
    {

        var observable = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(
            new() { ConnectionString = "initial", Timeout = 0 });
        
        var rules = new List<ConfigRule>
        {
            ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new())
        };

        // Longer debounce to test massive coalescing
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(200));
        TrackForDisposal(configManager);
        TrackForDisposal(observable);


        const int totalChanges = 200;
        var fireTask = Task.Run(async () =>
        {
            for (var i = 1; i <= totalChanges; i++)
            {
                observable.OnNext(new()
                { 
                    ConnectionString = $"change-{i}", 
                    Timeout = i 
                });
                await Task.Delay(1); // 1ms between changes = 200ms total (within debounce window)
            }
        });

        await fireTask;

        // Wait for final debounced result
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<DatabaseConfig>();
                return config?.ConnectionString == $"change-{totalChanges}";
            },
            timeout: TimeSpan.FromSeconds(3));


        var result = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(result);
        Assert.Equal($"change-{totalChanges}", result.ConnectionString);
        Assert.Equal(totalChanges, result.Timeout);
    }

    [Fact]
    [Trait("Type", "Stress")]
    public async Task ConfigManager_ConcurrentMultipleRules_ShouldMaintainRuleOrder()
    {

        var observables = new List<System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>>();
        var rules = new List<ConfigRule>();
        
        const int numRules = 20; // 20 competing rules
        
        for (var i = 0; i < numRules; i++)
        {
            var initialConfig = new DatabaseConfig 
            { 
                ConnectionString = $"rule-{i}-initial", 
                Timeout = i 
            };
            var observable = new System.Reactive.Subjects.BehaviorSubject<DatabaseConfig>(initialConfig);
            observables.Add(observable);
            
            rules.Add(ConfigRule.Create<ObservableProvider<DatabaseConfig>, ObservableProviderOptions<DatabaseConfig>, ObservableProviderQuery>(
                _ => new(observable),
                _ => ObservableProviderQuery.Default,
                typeof(DatabaseConfig),
                new()));
        }

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseDebounce(50));
        TrackForDisposal(configManager);
        foreach (var obs in observables)
        {
            TrackForDisposal(obs);
        }

        // Initial state - last rule should win
        var initialResult = configManager.GetConfig<DatabaseConfig>();
        await ActiveWaitHelpers.WaitUntilAsync(
            () => configManager.GetConfig<DatabaseConfig>()?.ConnectionString == $"rule-{numRules - 1}-initial",
            timeout: TimeSpan.FromSeconds(3),
            description: "initial last rule resolution");
        initialResult = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(initialResult);
        Assert.Equal($"rule-{numRules - 1}-initial", initialResult.ConnectionString);


        var updateTasks = observables.Select((obs, index) =>
            Task.Run(async () =>
            {
                for (var change = 1; change <= 10; change++)
                {
                    obs.OnNext(new()
                    { 
                        ConnectionString = $"rule-{index}-change-{change}", 
                        Timeout = index * 100 + change 
                    });
                    await Task.Delay(10); // Concurrent but spaced updates
                }
            })
        ).ToArray();

        await Task.WhenAll(updateTasks);

        // Wait for stabilization - last rule should still win
        await ActiveWaitHelpers.WaitUntilAsync(
            () => {
                var config = configManager.GetConfig<DatabaseConfig>();
                return config?.ConnectionString?.StartsWith($"rule-{numRules - 1}-change-10") == true;
            },
            timeout: TimeSpan.FromSeconds(8),
            description: "final last rule winning after all 10 changes" );


        var finalConfig = configManager.GetConfig<DatabaseConfig>();
        Assert.NotNull(finalConfig);
        Assert.StartsWith($"rule-{numRules - 1}-change-", finalConfig.ConnectionString);
        Assert.True(finalConfig.Timeout >= (numRules - 1) * 100, $"Expected timeout >= {(numRules - 1) * 100}, got {finalConfig.Timeout}");
    }

    #endregion
}
