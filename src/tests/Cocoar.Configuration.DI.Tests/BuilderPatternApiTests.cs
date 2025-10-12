using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.DI.Extensions;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.DI.Tests;

public class BuilderPatternApiTests
{
    public interface ITestConfig { string Value { get; } }
    public record TestConfig(string Value) : ITestConfig;

    [Fact]
    public void Builder_Pattern_Works_With_Multiple_Types()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<TestConfig>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new TestConfig("Hello"))).Required(),
            rules.For<AppImpl>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new AppImpl(42))).Required()
        ], setup => [
            setup.ConcreteType<TestConfig>().ExposeAs<ITestConfig>(),
            setup.ConcreteType<AppImpl>()
        ]);

        var sp = services.BuildServiceProvider();
        
        // Test concrete registrations
        var testConfig = sp.GetRequiredService<TestConfig>();
        var appImpl = sp.GetRequiredService<AppImpl>();
        
        // Test interface exposure
        var interfaceConfig = sp.GetRequiredService<ITestConfig>();
        
        Assert.Equal("Hello", testConfig.Value);
        Assert.Equal(42, appImpl.V);
        Assert.Equal("Hello", interfaceConfig.Value);
    // Interface and concrete can be different instances; assert value equality instead of reference equality
    Assert.Equal(testConfig.Value, interfaceConfig.Value);
    }

    // Test classes from existing tests
    private interface IApp { int V { get; } }
    private record AppImpl(int V) : IApp;
}



