using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Fluent.ProviderOptions;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;
using Cocoar.Configuration.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Cocoar.Configuration.Providers.StaticJsonProvider;

namespace Cocoar.Configuration.Tests;

/// <summary>
/// Tests that verify all README examples work correctly.
/// 
/// 🎯 PURPOSE:
/// - Ensures documentation stays accurate and examples remain functional
/// - Provides working code that developers can copy from their IDE
/// - Gives compile-time validation with intellisense and error highlighting
/// - Serves as executable documentation with proper imports and error handling
/// 
/// 💡 FOR DEVELOPERS:
/// If you're looking for working examples of the Cocoar.Configuration API,
/// this file contains tested, complete code that you can copy and adapt.
/// Each test corresponds to examples shown in the main README.md file.
/// </summary>
public class ReadmeExamplesTests
{
    #region README Example 1: Define Settings

    // This matches the README example exactly
    public interface IMySettings 
    { 
        bool Enabled { get; } 
        int Value { get; } 
    }

    public sealed class MySettings : IMySettings 
    { 
        public bool Enabled { get; set; } 
        public int Value { get; set; } 
    }

    #endregion

    [Fact]
    public void ReadmeExample_QuickStart_BasicConfigManager_Works()
    {
        // 📖 README EXAMPLE: Basic configuration with file + environment layers
        // This demonstrates the core pattern shown in the "Quick start" section
        
        // Create temporary JSON file for the example
        var tempJsonFile = Path.GetTempFileName();
        File.WriteAllText(tempJsonFile, """
        {
            "MySection": {
                "Enabled": true,
                "Value": 42
            }
        }
        """);

        // Set environment variable for override example
        Environment.SetEnvironmentVariable("MYAPP_Enabled", "false");

        try
        {
            // This code matches the README example exactly
            var rules = new []
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempJsonFile, "MySection"))
                    .For<MySettings>()
                    .As<IMySettings>()
                    .Build(),
                Rule.From.Environment(_ => new EnvironmentVariableRuleOptions(environmentPrefix: "MYAPP_"))
                    .For<MySettings>()
                    .As<IMySettings>()
                    .Build()
            };

            var manager = new ConfigManager(rules).Initialize();
            var cfg = manager.GetConfig<IMySettings>();

            // Verify the example works
            Assert.NotNull(cfg);
            Assert.False(cfg.Enabled); // Should be overridden by environment variable
            Assert.Equal(42, cfg.Value); // Should come from file
        }
        finally
        {
            // Cleanup
            File.Delete(tempJsonFile);
            Environment.SetEnvironmentVariable("MYAPP_Enabled", null);
        }
    }

    [Fact]
    public void ReadmeExample_AspNetCore_Integration_Works()
    {
        // 📖 README EXAMPLE: ASP.NET Core integration
        // Shows how to register Cocoar with the DI container
        
        // Create temporary JSON file
        var tempJsonFile = Path.GetTempFileName();
        File.WriteAllText(tempJsonFile, """
        {
            "MySection": {
                "Enabled": true,
                "Value": 123
            }
        }
        """);

        try
        {
            // This matches the README ASP.NET Core example
            var builder = WebApplication.CreateBuilder();
            
            var rules = new []
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempJsonFile, "MySection"))
                    .For<MySettings>()
                    .As<IMySettings>()
                    .Build()
            };

            builder.Services.AddCocoarConfiguration(rules);

            var app = builder.Build();
            
            // This matches the README example
            var cfg = app.Services.GetRequiredService<ConfigManager>().GetConfig<IMySettings>();

            // Verify it works
            Assert.NotNull(cfg);
            Assert.True(cfg.Enabled);
            Assert.Equal(123, cfg.Value);
        }
        finally
        {
            File.Delete(tempJsonFile);
        }
    }

    #region Dynamic Dependency Example Types

    public class MyHttpPollingSettings
    {
        public string Url { get; set; } = "";
    }

    public class MyCfg
    {
        public string Name { get; set; } = "";
        public bool Active { get; set; }
    }

    #endregion

    [Fact]
    public void ReadmeExample_DynamicDependency_Works()
    {
        // 📖 README EXAMPLE: Dynamic dependencies
        // Shows how later rules can read config from earlier rules during recompute
        
        // Create config file with URL for dynamic dependency
        var tempJsonFile = Path.GetTempFileName();
        File.WriteAllText(tempJsonFile, """
        {
            "Remote": {
                "Url": "/api/config"
            }
        }
        """);

        try
        {
            var builder = WebApplication.CreateBuilder();

            // This matches the README dynamic dependency example (simplified for testing)
            builder.Services.AddCocoarConfiguration([
                // Base settings providing the URL
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempJsonFile, "Remote"))
                     .For<MyHttpPollingSettings>()
                     .Required()
                     .Build(),

                // Static rule that reads the URL (simplified instead of HTTP for testing)
                Rule.From.Static(cm => new {
                        Name = $"Config from {cm.GetRequiredConfig<MyHttpPollingSettings>().Url}",
                        Active = true
                    })
                    .For<MyCfg>()
                    .Build()
            ]);

            var app = builder.Build();
            var configAccessor = app.Services.GetRequiredService<ConfigManager>();
            
            // Verify dynamic dependency works
            var httpSettings = configAccessor.GetConfig<MyHttpPollingSettings>();
            var dynamicConfig = configAccessor.GetConfig<MyCfg>();

            Assert.NotNull(httpSettings);
            Assert.Equal("/api/config", httpSettings.Url);
            
            Assert.NotNull(dynamicConfig);
            Assert.Contains("/api/config", dynamicConfig.Name);
            Assert.True(dynamicConfig.Active);
        }
        finally
        {
            File.Delete(tempJsonFile);
        }
    }

    [Fact]
    public void ReadmeExample_FileAndEnvironment_LayeredConfiguration_Works()
    {
        // 📖 README EXAMPLE: Layered configuration pattern  
        // Demonstrates how environment variables override file settings
        
        // This test verifies the layered configuration pattern shown in README
        var tempJsonFile = Path.GetTempFileName();
        File.WriteAllText(tempJsonFile, """
        {
            "MySection": {
                "Enabled": true,
                "Value": 100,
                "Secret": "from-file"
            }
        }
        """);

        // Environment override
        Environment.SetEnvironmentVariable("MYAPP_Value", "200");
        Environment.SetEnvironmentVariable("MYAPP_Secret", "from-env");

        try
        {
            var rules = new []
            {
                // File first (base layer)
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempJsonFile, "MySection"))
                    .For<MySettingsExtended>()
                    .As<IMySettingsExtended>()
                    .Build(),
                // Environment second (override layer)
                Rule.From.Environment(_ => new EnvironmentVariableRuleOptions(environmentPrefix: "MYAPP_"))
                    .For<MySettingsExtended>()
                    .As<IMySettingsExtended>()
                    .Build()
            };

            var manager = new ConfigManager(rules).Initialize();
            var cfg = manager.GetConfig<IMySettingsExtended>();

            Assert.NotNull(cfg);
            Assert.True(cfg.Enabled);        // From file (not overridden)
            Assert.Equal(200, cfg.Value);    // From environment (overridden)
            Assert.Equal("from-env", cfg.Secret); // From environment (overridden)
        }
        finally
        {
            File.Delete(tempJsonFile);
            Environment.SetEnvironmentVariable("MYAPP_Value", null);
            Environment.SetEnvironmentVariable("MYAPP_Secret", null);
        }
    }

    #region Extended Settings for layered example

    public interface IMySettingsExtended : IMySettings
    {
        string Secret { get; }
    }

    public sealed class MySettingsExtended : IMySettingsExtended
    {
        public bool Enabled { get; set; }
        public int Value { get; set; }
        public string Secret { get; set; } = "";
    }

    #endregion
}
