using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Tests;

public class ServiceLifetimeTests
{
    public interface IMyService
    {
        string Name { get; }
        int Value { get; }
    }

    public class MyServiceConfig : IMyService
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    [Fact]
    public void Should_Register_AsSingleton_Without_Key()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .AsSingleton<IMyService>()
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rule);
        var provider = services.BuildServiceProvider();

        // Assert
        var service1 = provider.GetRequiredService<IMyService>();
        var service2 = provider.GetRequiredService<IMyService>();
        
        Assert.Same(service1, service2); // Should be same instance (singleton)
        Assert.Equal("Test", service1.Name);
        Assert.Equal(42, service1.Value);
    }

    [Fact]
    public void Should_Register_AsScoped_Without_Key()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .AsScoped<IMyService>()
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rule);
        var provider = services.BuildServiceProvider();

        // Assert - Within same scope, should get same instance
        using var scope1 = provider.CreateScope();
        var service1A = scope1.ServiceProvider.GetRequiredService<IMyService>();
        var service1B = scope1.ServiceProvider.GetRequiredService<IMyService>();
        Assert.Same(service1A, service1B);

        // Assert - Different scopes should get different instances
        using var scope2 = provider.CreateScope();
        var service2 = scope2.ServiceProvider.GetRequiredService<IMyService>();
        Assert.NotSame(service1A, service2);
        
        Assert.Equal("Test", service1A.Name);
        Assert.Equal(42, service1A.Value);
    }

    [Fact]
    public void Should_Register_AsTransient_Without_Key()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .AsTransient<IMyService>()
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rule);
        var provider = services.BuildServiceProvider();

        // Assert
        var service1 = provider.GetRequiredService<IMyService>();
        var service2 = provider.GetRequiredService<IMyService>();
        
        Assert.NotSame(service1, service2); // Should be different instances (transient)
        Assert.Equal("Test", service1.Name);
        Assert.Equal(42, service1.Value);
    }

    [Fact]
    public void Should_Register_Multiple_Lifetimes_With_Keys()
    {
        // Arrange
        var builder = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .AsSingleton<IMyService>("singleton-key")
            .AsScoped<IMyService>("scoped-key")
            .AsTransient<IMyService>("transient-key");

        var rules = builder.BuildRules().ToList();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules);
        var provider = services.BuildServiceProvider();

        // Assert - Singleton
        var singleton1 = provider.GetRequiredKeyedService<IMyService>("singleton-key");
        var singleton2 = provider.GetRequiredKeyedService<IMyService>("singleton-key");
        Assert.Same(singleton1, singleton2);

        // Assert - Scoped (within scope)
        using var scope = provider.CreateScope();
        var scoped1 = scope.ServiceProvider.GetRequiredKeyedService<IMyService>("scoped-key");
        var scoped2 = scope.ServiceProvider.GetRequiredKeyedService<IMyService>("scoped-key");
        Assert.Same(scoped1, scoped2);

        // Assert - Transient
        var transient1 = provider.GetRequiredKeyedService<IMyService>("transient-key");
        var transient2 = provider.GetRequiredKeyedService<IMyService>("transient-key");
        Assert.NotSame(transient1, transient2);

        // All should have same config values
        Assert.Equal("Test", singleton1.Name);
        Assert.Equal("Test", scoped1.Name);
        Assert.Equal("Test", transient1.Name);
    }

    [Fact]
    public void Should_Throw_When_Registering_Same_Lifetime_Without_Key_Twice()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
                .For<MyServiceConfig>()
                .AsSingleton<IMyService>()
                .AsSingleton<IMyService>(); // Should throw
        });

        Assert.Contains("A Singleton registration without a key already exists", exception.Message);
    }

    [Fact]
    public void Should_Throw_When_Registering_Same_Lifetime_And_Key_Twice()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
                .For<MyServiceConfig>()
                .AsSingleton<IMyService>("same-key")
                .AsSingleton<IMyService>("same-key"); // Should throw
        });

        Assert.Contains("A Singleton registration with key 'same-key' already exists", exception.Message);
    }

    [Fact]
    public void Should_Allow_Same_Lifetime_With_Different_Keys()
    {
        // Arrange
        var builder = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .AsSingleton<IMyService>("key1")
            .AsSingleton<IMyService>("key2");

        var rules = builder.BuildRules().ToList();

        // Assert
        Assert.Equal(2, rules.Count);
        Assert.Equal("key1", rules[0].Registration.ServiceKey);
        Assert.Equal("key2", rules[1].Registration.ServiceKey);
    }

    [Fact]
    public void Should_Use_Default_Singleton_Registration_When_No_Explicit_Registration()
    {
        // Arrange & Act
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build(); // Should not throw - creates default singleton registration

        // Assert
        Assert.Equal(ServiceLifetime.Singleton, rule.Registration.ServiceLifetime);
        Assert.Null(rule.Registration.ServiceKey);
        Assert.Null(rule.Registration.ContractType);
        Assert.Equal(typeof(MyServiceConfig), rule.Registration.ConcreteType);
    }
}
