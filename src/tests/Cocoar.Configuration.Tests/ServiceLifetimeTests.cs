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
    public void Should_Register_As_Singleton_Without_Key()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .As<IMyService>() // Defaults to Singleton
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
    public void Should_Register_As_Scoped_Without_Key()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .As<IMyService>(ServiceLifetime.Scoped)
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
    public void Should_Register_As_Transient_Without_Key()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .As<IMyService>(ServiceLifetime.Transient)
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
            .As<IMyService>(ServiceLifetime.Singleton, "singleton-key")
            .As<IMyService>(ServiceLifetime.Scoped, "scoped-key")
            .As<IMyService>(ServiceLifetime.Transient, "transient-key");

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
                .As<IMyService>() // Defaults to Singleton
                .As<IMyService>(); // Should throw - same lifetime
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
                .As<IMyService>(ServiceLifetime.Singleton, "same-key")
                .As<IMyService>(ServiceLifetime.Singleton, "same-key"); // Should throw
        });

        Assert.Contains("A Singleton registration with key 'same-key' already exists", exception.Message);
    }

    [Fact]
    public void Should_Allow_Same_Lifetime_With_Different_Keys()
    {
        // Arrange
        var builder = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .As<IMyService>(ServiceLifetime.Singleton, "key1")
            .As<IMyService>(ServiceLifetime.Singleton, "key2");

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

    [Fact]
    public void Should_Default_To_Singleton_When_As_Called_Without_ServiceLifetime()
    {
        // Arrange & Act
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .As<IMyService>() // No ServiceLifetime specified - should default to Singleton
            .Build();

        // Assert
        Assert.Equal(ServiceLifetime.Singleton, rule.Registration.ServiceLifetime);
        Assert.Null(rule.Registration.ServiceKey);
        Assert.Equal(typeof(IMyService), rule.Registration.ContractType);
        Assert.Equal(typeof(MyServiceConfig), rule.Registration.ConcreteType);
    }

    [Fact]
    public void Should_Register_In_DI_With_Default_Singleton_Lifetime()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .As<IMyService>() // No ServiceLifetime specified
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule]);

        // Assert - Check DI registrations
        var concreteDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(MyServiceConfig));
        var interfaceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMyService));

        Assert.NotNull(concreteDescriptor);
        Assert.NotNull(interfaceDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, concreteDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, interfaceDescriptor.Lifetime);

        // Verify it actually works
        var serviceProvider = services.BuildServiceProvider();
        var instance1 = serviceProvider.GetRequiredService<IMyService>();
        var instance2 = serviceProvider.GetRequiredService<IMyService>();
        
        Assert.Same(instance1, instance2); // Should be the same instance (singleton behavior)
        Assert.Equal("Test", instance1.Name);
        Assert.Equal(42, instance1.Value);
    }
}
