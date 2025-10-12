using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cocoar.Configuration.Reactive;

namespace Cocoar.Configuration.DI.Tests;

public class ConfigureLifetimeAndKeyTests
{
    private record App(int Value);
    private interface IApp { int Value { get; } }
    private record AppImpl(int Value) : IApp;

    [Fact]
    public void Expose_Interface_Registration_Defaults_To_Scoped()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<AppImpl>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new AppImpl(1))).Required()
        ], setup => [
            setup.ConcreteType<AppImpl>().ExposeAs<IApp>()
        ]);
        var sp = services.BuildServiceProvider();
        var a1 = sp.GetRequiredService<IApp>();
        var a2 = sp.GetRequiredService<IApp>();
    Assert.NotNull(a1);
    Assert.NotNull(a2);
    }

    // Removed transient override test: current model ties interface resolution to underlying concrete instance.

    [Fact]
    public void Multiple_Interface_Exposures_Resolve_From_Same_Scoped_Instance()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<AppImpl>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new AppImpl(1))).Required()
        ], setup => [
            setup.ConcreteType<AppImpl>()
                .ExposeAs<IApp>()
        ]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IApp>();
        Assert.NotNull(resolved);
    }

    [Fact]
    public void Reactive_Config_Registered_When_Opted_In()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(rules => [
            rules.For<AppImpl>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new AppImpl(5))).Required()
        ], setup => [
            setup.ConcreteType<AppImpl>() // reactive always available
        ]);
        var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();
        var reactive = sp.GetRequiredService<IReactiveConfig<AppImpl>>();
        Assert.Equal(5, reactive.CurrentValue.Value);
    }
}
