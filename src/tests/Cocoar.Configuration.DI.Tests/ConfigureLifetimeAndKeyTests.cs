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
    public void Reactive_Config_Registered_When_Opted_In()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<AppImpl>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new AppImpl(5))).Required()
        ], setup => [
            setup.ConcreteType<AppImpl>() // reactive always available
        ]));
        var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();
        var reactive = sp.GetRequiredService<IReactiveConfig<AppImpl>>();
        Assert.Equal(5, reactive.CurrentValue.Value);
    }
}
