using Cocoar.Configuration.DI;
using Cocoar.Configuration.DI.Extensions;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

/// <summary>
/// Tests for exposed type registration.
///
/// IMPORTANT: With the Master Backplane architecture (v5.0+), configuration instances
/// are cached globally. All resolved services return the same cached instance.
/// </summary>
public class ExposedTypeRegistrationTests
{
    public interface ITestConfig { string Value { get; } }
    public record TestConfig(string Value) : ITestConfig;

    [Fact]
    public void ExposedType_Default_Returns_Same_Cached_Instance()
    {
        // With Master Backplane architecture, all scopes receive the same cached instance
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<TestConfig>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestConfig("Hello"))).Required()
        ], setup => [
            // Provide the concrete mapping so ConfigManager can resolve the interface
            setup.ConcreteType<TestConfig>().ExposeAs<ITestConfig>(),
            // No lifetime capability specified for the exposed type => defaults to Scoped
            setup.ExposedType<ITestConfig>()
        ]);

        using var sp = services.BuildServiceProvider();
        using var scope1 = sp.CreateScope();
        var scoped1a = scope1.ServiceProvider.GetRequiredService<ITestConfig>();
        var scoped1b = scope1.ServiceProvider.GetRequiredService<ITestConfig>();

        Assert.Same(scoped1a, scoped1b); // same within scope

        using var scope2 = sp.CreateScope();
        var scoped2 = scope2.ServiceProvider.GetRequiredService<ITestConfig>();

        // With Master Backplane, all instances are the same cached object
        Assert.Same(scoped1a, scoped2);
        Assert.Equal("Hello", scoped1a.Value);
        Assert.Equal("Hello", scoped2.Value);
    }

    [Fact]
    public void ExposedType_Can_Override_Lifetime_And_Add_Keyed_Registrations()
    {
        // With Master Backplane architecture, all instances are the same cached object
        // regardless of DI lifetime settings
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<TestConfig>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestConfig("Hello"))).Required()
        ], setup => [
            setup.ConcreteType<TestConfig>().ExposeAs<ITestConfig>(),
            // Override default (non-keyed) to Singleton, disable default (redundant when overriding), and add a keyed transient
            setup.ExposedType<ITestConfig>().AsSingleton().DisableAutoRegistration().AsTransient("my-key")
        ]);

        using var sp = services.BuildServiceProvider();

        // Non-keyed should be singleton
        var def1 = sp.GetRequiredService<ITestConfig>();
        var def2 = sp.GetRequiredService<ITestConfig>();
        Assert.Same(def1, def2);
        Assert.Equal("Hello", def1.Value);

        // Keyed transient - but with Master Backplane, still returns same cached instance
        var k1a = sp.GetRequiredKeyedService<ITestConfig>("my-key");
        var k1b = sp.GetRequiredKeyedService<ITestConfig>("my-key");
        Assert.Same(k1a, k1b); // Same cached instance
        Assert.Equal("Hello", k1a.Value);
        Assert.Equal("Hello", k1b.Value);
    }
}
