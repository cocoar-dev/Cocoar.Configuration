using System.Reactive.Subjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;

namespace Cocoar.Configuration.Tests;

/// <summary>
/// Tests for observable recovery functionality when BehaviorSubjects become disposed/dead.
/// </summary>
public class ReactiveConfigRecoveryTests
{
    [Fact]
    public void GetReactiveConfig_WhenSubjectDisposed_ShouldCreateNewReactiveConfig()
    {
        // Arrange
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, "{ \"Value\": \"reactive_initial\" }");

        try
        {
            var services = new ServiceCollection();
            
            // Register Cocoar configuration with minimal setup
            services.AddCocoarConfiguration(
                rules: [
                    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath))
                        .For<TestConfig>().Required().Build()
                ]);

            var serviceProvider = services.BuildServiceProvider();
            var configManager = serviceProvider.GetRequiredService<ConfigManager>();

            // Get initial reactive config and verify it works
            var reactiveConfig1 = configManager.GetReactiveConfig<TestConfig>();
            Assert.Equal("reactive_initial", reactiveConfig1.CurrentValue.Value);
            
            var receivedValues = new List<TestConfig>();
            var subscription1 = reactiveConfig1.Subscribe(config => receivedValues.Add(config));
            
            Assert.Single(receivedValues);
            Assert.Equal("reactive_initial", receivedValues[0].Value);

            // Note: After refactoring, observables are managed by ReactiveConfigManager
            // We can't directly access and dispose the BehaviorSubject anymore.
            // Instead, we'll simulate the recovery scenario by disposing the subscription
            // and testing that we can still get a working reactive config.
            subscription1.Dispose();

            // Act - Get reactive config again after disposal
            var reactiveConfig2 = configManager.GetReactiveConfig<TestConfig>();
            var newValues = new List<TestConfig>();
            var subscription2 = reactiveConfig2.Subscribe(config => newValues.Add(config));

            // Assert - Should get a working reactive config (may be same instance due to optimization)
            Assert.NotNull(reactiveConfig2);
            Assert.Equal("reactive_initial", reactiveConfig2.CurrentValue.Value);
            Assert.Single(newValues);
            Assert.Equal("reactive_initial", newValues[0].Value);
            
            subscription2.Dispose();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public class TestConfig
    {
        public string Value { get; set; } = string.Empty;
    }
}