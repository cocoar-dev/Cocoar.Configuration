using Cocoar.Configuration;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Tests.DI;

/// <summary>
/// Tests for automatic registration of IReactiveConfig&lt;T&gt; for all configuration types.
/// </summary>
public class AutoReactiveRegistrationTests
{
    private interface ITestSettings
    {
        string Name { get; }
        int Value { get; }
    }

    private class TestSettings : ITestSettings
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    private class OtherSettings
    {
        public bool Enabled { get; set; }
        public string Description { get; set; } = "";
    }

    [Fact]
    public void AddCocoarConfiguration_AutoRegistersReactiveConfigForConcreteTypes()
    {
        // Arrange - Use simpler setup that works
        var services = new ServiceCollection();
        var rules = new ConfigRule[]
        {
            Rule.From.Static<TestSettings>(_ => new TestSettings { Name = "Test", Value = 42 })
                .For<TestSettings>().Build()
        };

        // Act
        services.AddCocoarConfiguration(rules);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - IReactiveConfig<T> should be auto-registered for concrete types
        var testReactiveConfig = serviceProvider.GetService<IReactiveConfig<TestSettings>>();

        Assert.NotNull(testReactiveConfig);

        // Verify it works
        Assert.Equal("Test", testReactiveConfig.CurrentValue.Name);
        Assert.Equal(42, testReactiveConfig.CurrentValue.Value);
    }

    [Fact]
    public void AddCocoarConfiguration_AutoRegistersReactiveConfigForBoundInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        var rules = new ConfigRule[]
        {
            Rule.From.Static<TestSettings>(_ => new TestSettings { Name = "Test", Value = 42 })
                .For<TestSettings>().Build()
        };
        var bindings = new BindingSpec[]
        {
            Bind.Type<TestSettings>().To<ITestSettings>()
        };

        // Act
        services.AddCocoarConfiguration(rules, bindings);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - IReactiveConfig<T> should be auto-registered for bound interfaces
        var interfaceReactiveConfig = serviceProvider.GetService<IReactiveConfig<ITestSettings>>();
        var concreteReactiveConfig = serviceProvider.GetService<IReactiveConfig<TestSettings>>();

        Assert.NotNull(interfaceReactiveConfig);
        Assert.NotNull(concreteReactiveConfig);

        // Verify they work
        Assert.Equal("Test", interfaceReactiveConfig.CurrentValue.Name);
        Assert.Equal(42, interfaceReactiveConfig.CurrentValue.Value);
        Assert.Equal("Test", concreteReactiveConfig.CurrentValue.Name);
        Assert.Equal(42, concreteReactiveConfig.CurrentValue.Value);
    }

    [Fact]
    public void AddCocoarConfiguration_ReactiveConfigsRegisteredAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var rules = new ConfigRule[]
        {
            Rule.From.Static<TestSettings>(_ => new TestSettings { Name = "Test", Value = 42 })
                .For<TestSettings>().Build()
        };

        // Act
        services.AddCocoarConfiguration(rules);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Same instance should be returned each time (Singleton)
        var reactive1 = serviceProvider.GetService<IReactiveConfig<TestSettings>>();
        var reactive2 = serviceProvider.GetService<IReactiveConfig<TestSettings>>();

        Assert.NotNull(reactive1);
        Assert.NotNull(reactive2);
        Assert.Same(reactive1, reactive2); // Should be the same instance
    }

    [Fact]
    public void AddCocoarConfiguration_WithDisabledAutoReactiveRegistration_DoesNotRegisterReactiveConfigs()
    {
        // Arrange
        var services = new ServiceCollection();
        var rules = new ConfigRule[]
        {
            Rule.From.Static<TestSettings>(_ => new TestSettings { Name = "Test", Value = 42 })
                .For<TestSettings>().Build()
        };

        // Act - Disable auto-registration
        services.AddCocoarConfiguration(rules, configureServices: options =>
        {
            options.DisableAutoReactiveRegistration();
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert - IReactiveConfig<T> should NOT be registered
        var reactiveConfig = serviceProvider.GetService<IReactiveConfig<TestSettings>>();
        Assert.Null(reactiveConfig);

        // But regular config should still work
        var regularConfig = serviceProvider.GetService<TestSettings>();
        Assert.NotNull(regularConfig);
        Assert.Equal("Test", regularConfig.Name);
    }
}