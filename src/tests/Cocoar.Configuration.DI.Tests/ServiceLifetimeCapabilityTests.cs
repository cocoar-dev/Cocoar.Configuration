using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.DI.Extensions;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

/// <summary>
/// Tests for service lifetime capabilities.
///
/// IMPORTANT: With the Master Backplane architecture (v5.0+), configuration instances
/// are cached globally. This means:
/// - GetConfig always returns the same cached instance
/// - DI lifetime settings (Scoped/Transient) don't create new configuration instances
/// - The instance only changes when the configuration is recomputed (e.g., file change)
///
/// The DI lifetime still affects when the service is resolved within the container,
/// but the underlying configuration instance is always the same cached object.
/// </summary>
public class ServiceLifetimeCapabilityTests
{
    private record TestService(int Value);
    private interface ITestService { int Value { get; } }

    [Fact]
    public void AsSingleton_Should_Create_Same_Instance()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(42))).Required()
        ], setup => [
            setup.ConcreteType<TestService>().AsSingleton()
        ]));

        var sp = services.BuildServiceProvider();
        var instance1 = sp.GetRequiredService<TestService>();
        var instance2 = sp.GetRequiredService<TestService>();

        Assert.Same(instance1, instance2);
        Assert.Equal(42, instance1.Value);
    }

    [Fact]
    public void RegisterAs_Singleton_Should_Create_Same_Instance()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(42))).Required()
        ], setup => [
            setup.ConcreteType<TestService>().RegisterAs(ServiceLifetime.Singleton)
        ]));

        var sp = services.BuildServiceProvider();
        var instance1 = sp.GetRequiredService<TestService>();
        var instance2 = sp.GetRequiredService<TestService>();

        Assert.Same(instance1, instance2);
        Assert.Equal(42, instance1.Value);
    }

    [Fact]
    public void AsTransient_Returns_Same_Cached_Instance()
    {
        // With Master Backplane architecture, configuration instances are cached globally.
        // AsTransient affects DI container behavior but doesn't create new config instances.
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(123))).Required()
        ], setup => [
            setup.ConcreteType<TestService>().AsTransient()
        ]));

        var sp = services.BuildServiceProvider();
        var instance1 = sp.GetRequiredService<TestService>();
        var instance2 = sp.GetRequiredService<TestService>();

        // Both return the same cached instance from the backplane
        Assert.Same(instance1, instance2);
        Assert.Equal(123, instance1.Value);
    }

    [Fact]
    public void RegisterAs_Transient_Returns_Same_Cached_Instance()
    {
        // With Master Backplane architecture, configuration instances are cached globally.
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(123))).Required()
        ], setup => [
            setup.ConcreteType<TestService>().RegisterAs(ServiceLifetime.Transient)
        ]));

        var sp = services.BuildServiceProvider();
        var instance1 = sp.GetRequiredService<TestService>();
        var instance2 = sp.GetRequiredService<TestService>();

        // Both return the same cached instance from the backplane
        Assert.Same(instance1, instance2);
        Assert.Equal(123, instance1.Value);
    }

    [Fact]
    public void WithKey_Should_Register_Keyed_Service()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(999))).Required()
        ], setup => [
            setup.ConcreteType<TestService>().RegisterAs(ServiceLifetime.Scoped, "my-key")
        ]));

        var sp = services.BuildServiceProvider();
        var instance = sp.GetRequiredKeyedService<TestService>("my-key");

        Assert.NotNull(instance);
        Assert.Equal(999, instance.Value);
    }

    [Fact]
    public void Default_Registration_Returns_Same_Cached_Instance()
    {
        // With Master Backplane architecture, configuration instances are cached globally.
        // All scopes receive the same cached instance.
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(555))).Required()
        ], setup => [
            setup.ConcreteType<TestService>() // No explicit lifetime specified
        ]));

        var sp = services.BuildServiceProvider();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var instance1a = scope1.ServiceProvider.GetRequiredService<TestService>();
        var instance1b = scope1.ServiceProvider.GetRequiredService<TestService>();
        var instance2 = scope2.ServiceProvider.GetRequiredService<TestService>();

        // All instances are the same cached object from the backplane
        Assert.Same(instance1a, instance1b);
        Assert.Same(instance1a, instance2);
    }

    [Fact]
    public void AsSingletonWithKey_Should_Register_Singleton_Keyed_Service()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(777))).Required()
        ], setup => [
            setup.ConcreteType<TestService>().AsSingleton("singleton-key")
        ]));

        var sp = services.BuildServiceProvider();
        var instance1 = sp.GetRequiredKeyedService<TestService>("singleton-key");
        var instance2 = sp.GetRequiredKeyedService<TestService>("singleton-key");

        Assert.Same(instance1, instance2); // Should be same instance (singleton)
        Assert.Equal(777, instance1.Value);
    }

    [Fact]
    public void Skip_Should_Prevent_Service_Registration()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<TestService>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestService(888))).Required()
        ], setup => [
            setup.ConcreteType<TestService>().DisableAutoRegistration()
        ]));

        var sp = services.BuildServiceProvider();

        // Service should not be registered
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<TestService>());
    }
}
