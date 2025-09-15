using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Tests.Integration;

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
    public void ServiceLifetime_RegisterAsSingleton_WithoutKey_CreatesSharedInstance()
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
    public void ServiceLifetime_RegisterAsScoped_WithoutKey_CreatesInstancePerScope()
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

    [Fact]
    public void Should_Register_Concrete_Type_With_For_Method_ServiceLifetime()
    {
        // Arrange & Act
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>(ServiceLifetime.Scoped) // Register concrete type directly as Scoped
            .Build();

        // Assert
        Assert.Equal(ServiceLifetime.Scoped, rule.Registration.ServiceLifetime);
        Assert.Null(rule.Registration.ServiceKey);
        Assert.Equal(typeof(MyServiceConfig), rule.Registration.ContractType); // Should be same as ConcreteType
        Assert.Equal(typeof(MyServiceConfig), rule.Registration.ConcreteType);
    }

    [Fact]
    public void Should_Register_Concrete_Type_With_For_Method_And_Key()
    {
        // Arrange & Act
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>(ServiceLifetime.Transient, "my-key")
            .Build();

        // Assert
        Assert.Equal(ServiceLifetime.Transient, rule.Registration.ServiceLifetime);
        Assert.Equal("my-key", rule.Registration.ServiceKey);
        Assert.Equal(typeof(MyServiceConfig), rule.Registration.ContractType);
        Assert.Equal(typeof(MyServiceConfig), rule.Registration.ConcreteType);
    }

    [Fact]
    public void Should_Allow_For_With_Lifetime_Plus_As_Interface()
    {
        // Arrange & Act
        var builder = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>(ServiceLifetime.Scoped) // Register concrete as Scoped
            .As<IMyService>(ServiceLifetime.Singleton); // Also register interface as Singleton

        var rules = builder.BuildRules().ToList();

        // Assert
        Assert.Equal(2, rules.Count);

        var concreteRule = rules.First(r => r.Registration.ContractType == typeof(MyServiceConfig));
        var interfaceRule = rules.First(r => r.Registration.ContractType == typeof(IMyService));

        Assert.Equal(ServiceLifetime.Scoped, concreteRule.Registration.ServiceLifetime);
        Assert.Equal(ServiceLifetime.Singleton, interfaceRule.Registration.ServiceLifetime);
    }

    [Fact]
    public void Should_Allow_Multiple_For_Calls_With_Different_Keys()
    {
        // Arrange & Act
        var builder = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>(ServiceLifetime.Singleton, "key1")
            .For<MyServiceConfig>(ServiceLifetime.Singleton, "key2")
            .For<MyServiceConfig>(ServiceLifetime.Scoped, "key3");

        var rules = builder.BuildRules().ToList();

        // Assert
        Assert.Equal(3, rules.Count);

        var rule1 = rules.First(r => r.Registration.ServiceKey == "key1");
        var rule2 = rules.First(r => r.Registration.ServiceKey == "key2");
        var rule3 = rules.First(r => r.Registration.ServiceKey == "key3");

        Assert.Equal(ServiceLifetime.Singleton, rule1.Registration.ServiceLifetime);
        Assert.Equal(ServiceLifetime.Singleton, rule2.Registration.ServiceLifetime);
        Assert.Equal(ServiceLifetime.Scoped, rule3.Registration.ServiceLifetime);

        // All should register the concrete type as both ConcreteType and ContractType
        foreach (var rule in rules)
        {
            Assert.Equal(typeof(MyServiceConfig), rule.Registration.ConcreteType);
            Assert.Equal(typeof(MyServiceConfig), rule.Registration.ContractType);
        }
    }

    [Fact]
    public void Should_Allow_Multiple_For_Calls_With_Same_Key_But_Different_Lifetimes()
    {
        // Arrange & Act
        var builder = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>(ServiceLifetime.Singleton, "same-key")
            .For<MyServiceConfig>(ServiceLifetime.Scoped, "same-key")
            .For<MyServiceConfig>(ServiceLifetime.Transient, "same-key");

        var rules = builder.BuildRules().ToList();

        // Assert
        Assert.Equal(3, rules.Count);

        var singletonRule = rules.First(r => r.Registration.ServiceLifetime == ServiceLifetime.Singleton);
        var scopedRule = rules.First(r => r.Registration.ServiceLifetime == ServiceLifetime.Scoped);
        var transientRule = rules.First(r => r.Registration.ServiceLifetime == ServiceLifetime.Transient);

        Assert.Equal("same-key", singletonRule.Registration.ServiceKey);
        Assert.Equal("same-key", scopedRule.Registration.ServiceKey);
        Assert.Equal("same-key", transientRule.Registration.ServiceKey);
    }

    [Fact]
    public void Should_Throw_When_For_Called_With_Same_Lifetime_And_Key()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
                .For<MyServiceConfig>(ServiceLifetime.Singleton, "duplicate-key")
                .For<MyServiceConfig>(ServiceLifetime.Singleton, "duplicate-key"); // Should throw
        });

        Assert.Contains("A Singleton registration with key 'duplicate-key' already exists", exception.Message);
    }

    [Fact]
    public void Should_Register_All_Multiple_For_Calls_In_DI()
    {
        // Arrange
        var builder = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>(ServiceLifetime.Singleton, "key1")
            .For<MyServiceConfig>(ServiceLifetime.Scoped, "key2")
            .As<IMyService>(ServiceLifetime.Transient, "key3");

        var rules = builder.BuildRules().ToList();

        // Act - Register in DI
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules);

        // Assert - Check that all registrations are present in DI
        var concreteKey1 = services.FirstOrDefault(s =>
            s.ServiceType == typeof(MyServiceConfig) && s.ServiceKey?.ToString() == "key1");
        var concreteKey2 = services.FirstOrDefault(s =>
            s.ServiceType == typeof(MyServiceConfig) && s.ServiceKey?.ToString() == "key2");
        var interfaceKey3 =
            services.FirstOrDefault(s => s.ServiceType == typeof(IMyService) && s.ServiceKey?.ToString() == "key3");

        Assert.NotNull(concreteKey1);
        Assert.NotNull(concreteKey2);
        Assert.NotNull(interfaceKey3);

        Assert.Equal(ServiceLifetime.Singleton, concreteKey1.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, concreteKey2.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, interfaceKey3.Lifetime);

        // Verify they actually resolve
        var serviceProvider = services.BuildServiceProvider();
        var resolved1 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("key1");
        var resolved2 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("key2");
        var resolved3 = serviceProvider.GetRequiredKeyedService<IMyService>("key3");

        Assert.NotNull(resolved1);
        Assert.NotNull(resolved2);
        Assert.NotNull(resolved3);
    }
}
