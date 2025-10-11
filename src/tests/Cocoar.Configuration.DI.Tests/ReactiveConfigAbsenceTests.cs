using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

public class ReactiveConfigAbsenceTests
{
    private record Foo(int Number);

    private static ConfigRule Rule<T>(T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        return Providers.StaticJsonProvider.CreateRule<T>(json, required: true);
    }

    [Fact]
    public void Reactive_Not_Registered_Unless_Opted_In()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(_ => [Rule(new Foo(9))], setup => [
            setup.ConcreteType<Foo>()
        ]);
        var sp = services.BuildServiceProvider();
        // Now always registered
        var reactive = sp.GetRequiredService<IReactiveConfig<Foo>>();
        Assert.Equal(9, reactive.CurrentValue.Number);
    }
}



