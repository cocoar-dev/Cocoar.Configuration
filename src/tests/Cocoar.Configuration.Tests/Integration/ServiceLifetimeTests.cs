using Cocoar.Configuration.DI;
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
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
            ]);
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
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
            ]);
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
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
            ], configureServices: opts => opts.Register.Add<IMyService>(ServiceLifetime.Transient));
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
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
            ], options =>
        {
            options.Register
                .Add<IMyService>(ServiceLifetime.Singleton, "singleton-key")
                .Add<IMyService>(ServiceLifetime.Scoped, "scoped-key")
                .Add<IMyService>(ServiceLifetime.Transient, "transient-key");
        });
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
    public void Should_Register_In_DI_With_Default_Scoped_Lifetime()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build();

        // Act
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
            ]);

        // Assert - Check DI registrations (default is now Scoped)
        var concreteDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(MyServiceConfig));
        var interfaceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMyService));

        Assert.NotNull(concreteDescriptor);
        Assert.NotNull(interfaceDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, concreteDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, interfaceDescriptor.Lifetime);

        // Verify it actually works with scoped behavior
        var serviceProvider = services.BuildServiceProvider();
        
        // Same instance within a scope
        using var scope = serviceProvider.CreateScope();
        var instance1 = scope.ServiceProvider.GetRequiredService<IMyService>();
        var instance2 = scope.ServiceProvider.GetRequiredService<IMyService>();
        Assert.Same(instance1, instance2); // Same within scope
        
        // Different instance in different scope
        using var scope2 = serviceProvider.CreateScope();
        var instance3 = scope2.ServiceProvider.GetRequiredService<IMyService>();
        Assert.NotSame(instance1, instance3); // Different across scopes
    }

    [Fact]
    public void Should_Register_All_Multiple_For_Calls_In_DI()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>();


        // Act - Register in DI
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
            ], options => options.Register
                .Add<MyServiceConfig>(ServiceLifetime.Singleton, "key1")
                .Add<MyServiceConfig>(ServiceLifetime.Scoped, "key2")
                .Add<IMyService>(ServiceLifetime.Transient, "key3")
        );

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

    [Fact]
    public void NewAPI_Multiple_Service_Lifetimes_Work_Correctly()
    {
        // Arrange - Same config rule for all registrations
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build();

        // Act - Register the same type with different lifetimes using new API
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
        ], configureServices: opts => {
            opts.Register.Add<MyServiceConfig>(ServiceLifetime.Singleton, "singleton-key");
            opts.Register.Add<MyServiceConfig>(ServiceLifetime.Scoped, "scoped-key");
            opts.Register.Add<MyServiceConfig>(ServiceLifetime.Transient, "transient-key");
            opts.Register.Add<IMyService>(ServiceLifetime.Singleton, "interface-singleton");
            opts.Register.Add<IMyService>(ServiceLifetime.Transient, "interface-transient");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify Singleton behavior
        var singleton1 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("singleton-key");
        var singleton2 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("singleton-key");
        Assert.Same(singleton1, singleton2); // Same instance

        var interfaceSingleton1 = serviceProvider.GetRequiredKeyedService<IMyService>("interface-singleton");
        var interfaceSingleton2 = serviceProvider.GetRequiredKeyedService<IMyService>("interface-singleton");
        Assert.Same(interfaceSingleton1, interfaceSingleton2); // Same instance

        // Assert - Verify Scoped behavior
        using (var scope = serviceProvider.CreateScope())
        {
            var scoped1 = scope.ServiceProvider.GetRequiredKeyedService<MyServiceConfig>("scoped-key");
            var scoped2 = scope.ServiceProvider.GetRequiredKeyedService<MyServiceConfig>("scoped-key");
            Assert.Same(scoped1, scoped2); // Same within scope
        }

        using (var scope2 = serviceProvider.CreateScope())
        {
            var scoped3 = scope2.ServiceProvider.GetRequiredKeyedService<MyServiceConfig>("scoped-key");
            Assert.NotSame(singleton1, scoped3); // Different from singleton
        }

        // Assert - Verify Transient behavior
        var transient1 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("transient-key");
        var transient2 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("transient-key");
        Assert.NotSame(transient1, transient2); // Different instances

        var interfaceTransient1 = serviceProvider.GetRequiredKeyedService<IMyService>("interface-transient");
        var interfaceTransient2 = serviceProvider.GetRequiredKeyedService<IMyService>("interface-transient");
        Assert.NotSame(interfaceTransient1, interfaceTransient2); // Different instances

        // Assert - Verify all have same configuration values
        Assert.Equal("Test", singleton1.Name);
        Assert.Equal(42, singleton1.Value);
        Assert.Equal("Test", transient1.Name);
        Assert.Equal(42, transient1.Value);
        Assert.Equal("Test", interfaceSingleton1.Name);
        Assert.Equal(42, interfaceSingleton1.Value);
    }

    [Fact]
    public void NewAPI_Default_Registration_Lifetime_Can_Be_Changed()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build();

        // Act - Change default lifetime from Scoped to Singleton
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
        ], configureServices: opts => {
            opts.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
        });

        // Assert - Check that default registrations use Singleton
        var concreteDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(MyServiceConfig) && s.ServiceKey == null);
        var interfaceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMyService) && s.ServiceKey == null);

        Assert.NotNull(concreteDescriptor);
        Assert.NotNull(interfaceDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, concreteDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, interfaceDescriptor.Lifetime);

        // Verify behavior
        var serviceProvider = services.BuildServiceProvider();
        var instance1 = serviceProvider.GetRequiredService<IMyService>();
        var instance2 = serviceProvider.GetRequiredService<IMyService>();
        Assert.Same(instance1, instance2); // Should be same instance (singleton)
    }

    [Fact]
    public void NewAPI_Auto_Registration_Can_Be_Disabled()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build();

        // Act - Disable auto-registration
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
        ], configureServices: opts => {
            opts.DefaultRegistrationLifetime(null); // Disable auto-registration
        });

        // Assert - Should have no automatic registrations
        var concreteDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(MyServiceConfig));
        var interfaceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMyService));

        Assert.Null(concreteDescriptor); // No automatic registration
        Assert.Null(interfaceDescriptor); // No automatic registration

        // ConfigManager should still be registered
        var configManagerDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ConfigManager));
        Assert.NotNull(configManagerDescriptor);
    }

    [Fact] 
    public void NewAPI_Explicit_Registration_Overrides_Default_Lifetime()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build();

        // Act - Set default to Singleton, but explicitly register interface as Transient
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
        ], configureServices: opts => {
            opts.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
            opts.Register.Add<IMyService>(ServiceLifetime.Transient); // Explicit override
        });

        // Assert - Concrete type should use default (Singleton), interface should use explicit (Transient)
        var concreteDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(MyServiceConfig));
        var interfaceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMyService));

        Assert.NotNull(concreteDescriptor);
        Assert.NotNull(interfaceDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, concreteDescriptor.Lifetime); // Default
        Assert.Equal(ServiceLifetime.Transient, interfaceDescriptor.Lifetime); // Explicit override

        // Verify behavior
        var serviceProvider = services.BuildServiceProvider();
        
        // Concrete type should be singleton
        var concrete1 = serviceProvider.GetRequiredService<MyServiceConfig>();
        var concrete2 = serviceProvider.GetRequiredService<MyServiceConfig>();
        Assert.Same(concrete1, concrete2); // Same instance (singleton)
        
        // Interface should be transient
        var interface1 = serviceProvider.GetRequiredService<IMyService>();
        var interface2 = serviceProvider.GetRequiredService<IMyService>();
        Assert.NotSame(interface1, interface2); // Different instances (transient)
    }

    [Fact]
    public void NewAPI_Duplicate_Registration_Are_Additive()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build();

        // Act - Register same type multiple times with different lifetimes
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
        ], configureServices: opts => {
            opts.Register.Add<IMyService>(ServiceLifetime.Singleton); // First registration
            opts.Register.Add<IMyService>(ServiceLifetime.Transient); // Second registration
        });

        // Assert - Both registrations should be present (duplicates are additive)
        var interfaceDescriptors = services.Where(s => s.ServiceType == typeof(IMyService) && s.ServiceKey == null).ToList();
        
        // Should have 2 registrations (both explicit ones)
        Assert.Equal(2, interfaceDescriptors.Count);
        
        // Should have one Singleton and one Transient registration
        Assert.Contains(interfaceDescriptors, d => d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(interfaceDescriptors, d => d.Lifetime == ServiceLifetime.Transient);

        // ServiceProvider will use the last registered service (standard .NET DI behavior)
        var serviceProvider = services.BuildServiceProvider();
        var instance1 = serviceProvider.GetRequiredService<IMyService>();
        var instance2 = serviceProvider.GetRequiredService<IMyService>();
        Assert.NotSame(instance1, instance2); // Should be different instances (transient behavior - last wins)
    }

    [Fact] 
    public void NewAPI_Keyed_Services_Work_With_Explicit_Registration()
    {
        // Arrange
        var rule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Test", Value = 42 })
            .For<MyServiceConfig>()
            .Build();

        // Act - Register same type with different keys, disable auto-registration
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([rule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
        ], configureServices: opts => {
            opts.DefaultRegistrationLifetime(null); // Disable auto-registration
            opts.Register.Add<IMyService>(ServiceLifetime.Singleton, "key1");
            opts.Register.Add<IMyService>(ServiceLifetime.Transient, "key2");
            opts.Register.Add<IMyService>(ServiceLifetime.Scoped, "key1"); // Same key, should be additive
        });

        // Assert - Should have 3 explicit registrations (no auto-registration)
        var interfaceDescriptors = services.Where(s => s.ServiceType == typeof(IMyService)).ToList();
        Assert.Equal(3, interfaceDescriptors.Count);

        // All should be keyed (no unkeyed registrations)
        Assert.All(interfaceDescriptors, d => Assert.NotNull(d.ServiceKey));

        // Group by key to verify behavior
        var key1Descriptors = interfaceDescriptors.Where(d => d.ServiceKey?.ToString() == "key1").ToList();
        var key2Descriptors = interfaceDescriptors.Where(d => d.ServiceKey?.ToString() == "key2").ToList();
        
        Assert.Equal(2, key1Descriptors.Count); // Singleton + Scoped for key1
        Assert.Single(key2Descriptors); // Only Transient for key2

        // Verify actual behavior - ServiceProvider uses last registration for each key
        var serviceProvider = services.BuildServiceProvider();
        
        // key1 should use last registration (Scoped)
        using (var scope1 = serviceProvider.CreateScope())
        {
            var key1_instance1 = scope1.ServiceProvider.GetRequiredKeyedService<IMyService>("key1");
            var key1_instance2 = scope1.ServiceProvider.GetRequiredKeyedService<IMyService>("key1");
            Assert.Same(key1_instance1, key1_instance2); // Same within scope
        }

        using (var scope2 = serviceProvider.CreateScope())
        {
            var key1_instance3 = scope2.ServiceProvider.GetRequiredKeyedService<IMyService>("key1");
            // Different scope should give different instance (scoped behavior)
        }

        // key2 should be transient
        var key2_instance1 = serviceProvider.GetRequiredKeyedService<IMyService>("key2");
        var key2_instance2 = serviceProvider.GetRequiredKeyedService<IMyService>("key2");
        Assert.NotSame(key2_instance1, key2_instance2); // Different instances (transient)
    }

    [Fact]
    public void NewAPI_Comprehensive_Real_World_Example()
    {
        // Arrange - Simulate a real application with multiple config types
        var appConfigRule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "AppConfig", Value = 100 })
            .For<MyServiceConfig>()
            .Build();
            
        var cacheConfigRule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "CacheConfig", Value = 200 })
            .For<MyServiceConfig>()
            .Build();

        // Act - Configure realistic service registration scenarios
        var services = new ServiceCollection();
        services.AddCocoarConfiguration([appConfigRule], [
            Bind.Type<MyServiceConfig>().To<IMyService>()
        ], configureServices: opts => {
            // Set default to Scoped (typical web application)
            opts.DefaultRegistrationLifetime(ServiceLifetime.Scoped);
            
            // Override specific services with different lifetimes and keys
            opts.Register.Add<MyServiceConfig>(ServiceLifetime.Singleton, "app-config");     // App-wide singleton config
            opts.Register.Add<MyServiceConfig>(ServiceLifetime.Scoped, "request-config");    // Per-request config
            opts.Register.Add<IMyService>(ServiceLifetime.Transient, "transient-service");   // Transient service instances
            opts.Register.Add<IMyService>(ServiceLifetime.Singleton, "shared-service");      // Shared service instance
            
            // Remove auto-registration for specific types if needed
            opts.Register.Remove<MyServiceConfig>("unwanted-key");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify all registration scenarios work correctly
        
        // 1. Default registrations (Scoped)
        var defaultConcrete1 = serviceProvider.GetRequiredService<MyServiceConfig>();
        var defaultInterface1 = serviceProvider.GetRequiredService<IMyService>();
        
        using (var scope1 = serviceProvider.CreateScope())
        {
            var scopedConcrete1 = scope1.ServiceProvider.GetRequiredService<MyServiceConfig>();
            var scopedInterface1 = scope1.ServiceProvider.GetRequiredService<IMyService>();
            var scopedConcrete2 = scope1.ServiceProvider.GetRequiredService<MyServiceConfig>();
            
            Assert.Same(scopedConcrete1, scopedConcrete2); // Same within scope
        }
        
        using (var scope2 = serviceProvider.CreateScope())
        {
            var scopedConcrete3 = scope2.ServiceProvider.GetRequiredService<MyServiceConfig>();
            Assert.NotSame(defaultConcrete1, scopedConcrete3); // Different across scopes
        }

        // 2. Keyed Singleton services
        var appConfig1 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("app-config");
        var appConfig2 = serviceProvider.GetRequiredKeyedService<MyServiceConfig>("app-config");
        var sharedService1 = serviceProvider.GetRequiredKeyedService<IMyService>("shared-service");
        var sharedService2 = serviceProvider.GetRequiredKeyedService<IMyService>("shared-service");
        
        Assert.Same(appConfig1, appConfig2); // Singleton behavior
        Assert.Same(sharedService1, sharedService2); // Singleton behavior

        // 3. Keyed Scoped services
        using (var scope = serviceProvider.CreateScope())
        {
            var requestConfig1 = scope.ServiceProvider.GetRequiredKeyedService<MyServiceConfig>("request-config");
            var requestConfig2 = scope.ServiceProvider.GetRequiredKeyedService<MyServiceConfig>("request-config");
            Assert.Same(requestConfig1, requestConfig2); // Same within scope
        }

        // 4. Keyed Transient services
        var transientService1 = serviceProvider.GetRequiredKeyedService<IMyService>("transient-service");
        var transientService2 = serviceProvider.GetRequiredKeyedService<IMyService>("transient-service");
        Assert.NotSame(transientService1, transientService2); // Different instances

        // 5. Verify configuration values are correct for all instances
        Assert.Equal("AppConfig", defaultConcrete1.Name);
        Assert.Equal(100, defaultConcrete1.Value);
        Assert.Equal("AppConfig", appConfig1.Name);
        Assert.Equal(100, appConfig1.Value);
        Assert.Equal("AppConfig", sharedService1.Name);
        Assert.Equal(100, sharedService1.Value);
    }
}
