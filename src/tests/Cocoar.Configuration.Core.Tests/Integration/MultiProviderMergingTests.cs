using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using static Cocoar.Configuration.Core.Tests.Integration.MultiProviderTestModels;

namespace Cocoar.Configuration.Core.Tests.Integration;
[Trait("Category", "Integration")]
[Trait("Component", "ConfigManager")]
public class MultiProviderMergingTests
{
    #region Last-Write-Wins Semantics Tests

    #region Last-Write-Wins Semantics Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public void ConfigManager_StaticPlusObservable_LastRuleWins()
    {

        var baseConfig = """
        {
            "Name": "BaseApp",
            "Version": 1,
            "Database": {
                "ConnectionString": "server=base;",
                "Timeout": 30,
                "EnableRetry": true
            },
            "Features": {
                "EnableNewUI": false,
                "EnableLogging": true,
                "LogLevel": "Info"
            }
        }
        """;

        var overrideConfigJson = """
        {
            "Name": "OverriddenApp",
            "Version": 2,
            "Database": {
                "ConnectionString": "server=override;",
                "EnableRetry": false
            },
            "Features": {
                "EnableNewUI": true,
                "LogLevel": "Debug"
            }
        }
        """;

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<AppConfig>(baseConfig),      // Rule 0 (base)
            TestRules.StaticJson<AppConfig>(overrideConfigJson)  // Rule 1 (wins)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules));
        var config = configManager.GetConfig<AppConfig>();
        Assert.NotNull(config);

        Assert.Equal("OverriddenApp", config!.Name);           // From Rule 1 (override)
    Assert.Equal(2, config.Version);                     // From Rule 1 (override)
    Assert.Equal("server=override;", config.Database.ConnectionString); // From Rule 1 (override)
    Assert.Equal(30, config.Database.Timeout);           // From Rule 0 (not overridden in Rule 1)
    Assert.False(config.Database.EnableRetry);           // From Rule 1 (override)
    Assert.True(config.Features.EnableNewUI);            // From Rule 1 (override)
    Assert.True(config.Features.EnableLogging);          // From Rule 0 (not overridden in Rule 1)
    Assert.Equal("Debug", config.Features.LogLevel);     // From Rule 1 (override)
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public void ConfigManager_ObservablePlusStatic_StaticWins()
    {

        var baseConfig = """
        {
            "Name": "StaticApp",
            "Version": 99,
            "Features": {
                "EnableNewUI": false,
                "LogLevel": "Error"
            }
        }
        """;

        var observableConfig = new AppConfig
        {
            Name = "ObservableApp",
            Version = 1,
            Features = new()
            {
                EnableNewUI = true,
                LogLevel = "Debug"
            }
        };

        var behaviorSubject = new BehaviorSubject<AppConfig>(observableConfig);

        var rules = new List<ConfigRule>
        {
            TestRules.Observable<AppConfig>(behaviorSubject),  // Rule 0 (base)
            TestRules.StaticJson<AppConfig>(baseConfig)        // Rule 1 (wins!)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules));
        var config = configManager.GetConfig<AppConfig>();
        Assert.NotNull(config);

        Assert.Equal("StaticApp", config!.Name);      // From Static (wins)
    Assert.Equal(99, config.Version);           // From Static (wins)  
        Assert.False(config.Features.EnableNewUI);  // From Static (wins)
        Assert.Equal("Error", config.Features.LogLevel); // From Static (wins)
    }

    #endregion

    #endregion

    #region Complex Configuration Tests

    #region Complex Configuration Tests
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public void ConfigManager_NestedObjectMerging_MergesCorrectly()
    {

        var staticConfig = """
        {
            "Name": "StaticApp",
            "Version": 1,
            "Database": {
                "ConnectionString": "server=static;database=main;",
                "Timeout": 60,
                "EnableRetry": true
            },
            "Features": {
                "EnableNewUI": false,
                "EnableLogging": true,
                "LogLevel": "Info"
            }
        }
        """;        var observablePartialJson = """
        {
            "Database": {
                "ConnectionString": "server=prod;database=main;",
                "Timeout": 120
            },
            "Features": {
                "EnableNewUI": true,
                "LogLevel": "Debug"
            }
        }
        """;

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<AppConfig>(staticConfig),
            TestRules.StaticJson<AppConfig>(observablePartialJson)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules));
        var config = configManager.GetConfig<AppConfig>();
        Assert.NotNull(config);

        Assert.Equal("StaticApp", config.Name);                           // From Static (not overridden)
        Assert.Equal(1, config.Version);                                 // From Static (not overridden)
        Assert.Equal("server=prod;database=main;", config.Database.ConnectionString); // From Rule 1
        Assert.Equal(120, config.Database.Timeout);                     // From Rule 1
        Assert.True(config.Database.EnableRetry);                       // From Static (not overridden)
        Assert.True(config.Features.EnableNewUI);                       // From Rule 1
        Assert.True(config.Features.EnableLogging);                     // From Static (not overridden)
        Assert.Equal("Debug", config.Features.LogLevel);                // From Rule 1
    }
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "ConfigManager")]
    public void ConfigManager_ThreeProviders_LayersCorrectly()
    {

        var baseConfig = """{"Name": "Base", "Version": 1, "Features": {"LogLevel": "Info"}}""";
        var finalConfig = """{"Version": 999, "Features": {"LogLevel": "Error", "EnableNewUI": true}}""";

        var observableOverride = new
        {
            Name = "Observable",
            Features = new { LogLevel = "Debug" }
        };

        var behaviorSubject = new BehaviorSubject<string>(System.Text.Json.JsonSerializer.Serialize(observableOverride));

        var rules = new List<ConfigRule>
        {
            TestRules.StaticJson<AppConfig>(baseConfig),        // Rule 0: Base
            TestRules.ObservableString<AppConfig>(behaviorSubject),   // Rule 1: Override
            TestRules.StaticJson<AppConfig>(finalConfig)        // Rule 2: Final (wins!)
        };

        var configManager = ConfigManager.Create(c => c.UseConfiguration(rules));
        var config = configManager.GetConfig<AppConfig>();
        Assert.NotNull(config);

        Assert.Equal("Observable", config.Name);                // From Observable (rule 1, no conflict with rule 2)
        Assert.Equal(999, config.Version);                     // From Final (rule 2, wins over all)
        Assert.Equal("Error", config.Features.LogLevel);       // From Final (rule 2, wins over all)
        Assert.True(config.Features.EnableNewUI);              // From Final (rule 2, only provider)
    }

    #endregion

    #endregion
}

