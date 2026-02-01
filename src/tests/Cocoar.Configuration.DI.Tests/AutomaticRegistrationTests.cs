using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.DI.Extensions;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Reactive;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

/// <summary>
/// Tests for automatic registration of configuration types from rules without explicit setup.ConcreteType() calls.
/// This validates backward compatibility - types should be auto-registered just by having a rule.
/// </summary>
public class AutomaticRegistrationTests
{
    private record AppConfig(string Name, int Version);
    private record DatabaseConfig(string Host, int Port);
    private record FeatureFlags(bool EnableNewUI, bool EnableBetaFeatures);

    [Fact]
    public void Should_AutoRegister_Type_From_Rule_Without_Explicit_Setup()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act - Register WITHOUT setup.ConcreteType<>() call
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required()
        ]);
        
        // Assert - Type should still be injectable
        var sp = services.BuildServiceProvider();
        var config = sp.GetService<AppConfig>();
        
        Assert.NotNull(config);
        Assert.Equal("TestApp", config.Name);
        Assert.Equal(1, config.Version);
    }

    [Fact]
    public void Should_AutoRegister_Multiple_Types_From_Rules()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act - Multiple rules, no explicit setup
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required(),
            rules.For<DatabaseConfig>().FromStaticJson("{\"Host\":\"localhost\",\"Port\":5432}").Required(),
            rules.For<FeatureFlags>().FromStaticJson("{\"EnableNewUI\":true,\"EnableBetaFeatures\":false}").Required()
        ]);
        
        // Assert - All types should be injectable
        var sp = services.BuildServiceProvider();
        
        var appConfig = sp.GetService<AppConfig>();
        var dbConfig = sp.GetService<DatabaseConfig>();
        var features = sp.GetService<FeatureFlags>();
        
        Assert.NotNull(appConfig);
        Assert.NotNull(dbConfig);
        Assert.NotNull(features);
        
        Assert.Equal("TestApp", appConfig.Name);
        Assert.Equal("localhost", dbConfig.Host);
        Assert.True(features.EnableNewUI);
    }

    [Fact]
    public void AutoRegistered_Types_Should_Use_Scoped_Lifetime_By_Default()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required()
        ]);
        
        // Assert - Check that AppConfig is registered as Scoped
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(AppConfig));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AutoRegistered_Types_Should_Have_Same_Cached_Instance_Across_Scopes()
    {
        // With Master Backplane architecture, configuration instances are cached globally.
        // All scopes receive the same cached instance.
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required()
        ]);

        var sp = services.BuildServiceProvider();

        // Act - Get instances from different scopes
        AppConfig? instance1;
        AppConfig? instance2;

        using (var scope1 = sp.CreateScope())
        {
            instance1 = scope1.ServiceProvider.GetRequiredService<AppConfig>();
        }

        using (var scope2 = sp.CreateScope())
        {
            instance2 = scope2.ServiceProvider.GetRequiredService<AppConfig>();
        }

        // Assert - Same cached instance across scopes (Master Backplane behavior)
        Assert.Same(instance1, instance2);
        Assert.Equal("TestApp", instance1.Name);
        Assert.Equal(1, instance1.Version);
    }

    [Fact]
    public void AutoRegistered_Types_Should_Have_Same_Instance_Within_Scope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required()
        ]);
        
        var sp = services.BuildServiceProvider();
        
        // Act - Get instances from same scope
        using var scope = sp.CreateScope();
        var instance1 = scope.ServiceProvider.GetRequiredService<AppConfig>();
        var instance2 = scope.ServiceProvider.GetRequiredService<AppConfig>();
        
        // Assert - Same instance within scope (Scoped behavior)
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Explicit_Setup_Should_Override_AutoRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act - Explicit setup.ConcreteType() should take precedence
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required()
        ], setup => [
            setup.ConcreteType<AppConfig>().AsSingleton()
        ]);
        
        var sp = services.BuildServiceProvider();
        
        // Act - Get instances
        var instance1 = sp.GetRequiredService<AppConfig>();
        var instance2 = sp.GetRequiredService<AppConfig>();
        
        // Assert - Should be Singleton (not default Scoped)
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Should_Register_IReactiveConfig_For_AutoRegistered_Types()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required()
        ]);
        
        // Assert - IReactiveConfig<T> should also be available
        var sp = services.BuildServiceProvider();
        var reactiveConfig = sp.GetService<IReactiveConfig<AppConfig>>();
        
        Assert.NotNull(reactiveConfig);
        
        var currentValue = reactiveConfig.CurrentValue;
        Assert.Equal("TestApp", currentValue.Name);
        Assert.Equal(1, currentValue.Version);
    }

    [Fact]
    public void DisableAutoRegistration_Should_Prevent_Registration()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act - Explicitly disable auto-registration
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"TestApp\",\"Version\":1}").Required()
        ], setup => [
            setup.ConcreteType<AppConfig>().DisableAutoRegistration()
        ]);
        
        // Assert - Type should NOT be registered
        var sp = services.BuildServiceProvider();
        var config = sp.GetService<AppConfig>();
        
        Assert.Null(config);
        
        // But ConfigManager should still work
        var manager = sp.GetRequiredService<ConfigManager>();
        var manualConfig = manager.GetConfig<AppConfig>();
        Assert.NotNull(manualConfig);
        Assert.Equal("TestApp", manualConfig.Name);
    }

    [Fact]
    public void Should_AutoRegister_Types_With_Multiple_Rules()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act - Same type from multiple rules (layering)
        services.AddCocoarConfiguration(rules => [
            rules.For<AppConfig>().FromStaticJson("{\"Name\":\"BaseApp\",\"Version\":1}").Required(),
            rules.For<AppConfig>().FromStaticJson("{\"Version\":2}").Required() // Overrides Version
        ]);
        
        // Assert - Should get merged config
        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<AppConfig>();
        
        Assert.NotNull(config);
        Assert.Equal("BaseApp", config.Name);
        Assert.Equal(2, config.Version); // Last rule wins
    }
}
