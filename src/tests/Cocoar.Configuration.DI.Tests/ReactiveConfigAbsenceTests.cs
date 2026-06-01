using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Reactive;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

public class ReactiveConfigAbsenceTests
{
    private record Foo(int Number);

    [Fact]
    public void Reactive_Not_Registered_Unless_Opted_In()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
            rules.For<Foo>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(new Foo(9))).Required()
        ], setup => [
            setup.ConcreteType<Foo>()
        ]));
        var sp = services.BuildServiceProvider();
        // Now always registered
        var reactive = sp.GetRequiredService<IReactiveConfig<Foo>>();
        Assert.Equal(9, reactive.CurrentValue.Number);
    }
}



