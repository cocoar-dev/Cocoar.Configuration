using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

public class TupleReactiveConfigGuardTests
{
    private interface IApp { int V { get; } }
    private record App(int V) : IApp;

    private static ConfigRule Rule<T>(T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        return global::Cocoar.Configuration.Providers.StaticJsonProvider.CreateRule<T>(json, required: true);
    }

    [Fact]
    public void Tuple_With_Unconfigured_Primitive_Fails()
    {
        var mgr = new ConfigManager(new[]{ Rule(new App(1)) }, logger: NullLogger.Instance).Initialize();
        // int is not a configured type; guard should throw
        Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(App,int)>());
    }

    [Fact]
    public void Tuple_With_Bound_Interface_Succeeds()
    {
    var binding = Bind.Type<App>().To<IApp>();
    var mgr = new ConfigManager(new[]{ Rule(new App(2)) }, new[]{ (BindingSpec)binding }, NullLogger.Instance).Initialize();
        var cfg = mgr.GetReactiveConfig<(IApp,App)>();
        var snapshot = cfg.CurrentValue;
        Assert.Equal(2, snapshot.Item1.V);
        Assert.Equal(2, snapshot.Item2.V);
    }

    [Fact]
    public void Tuple_With_Unbound_Interface_Fails()
    {
        var mgr = new ConfigManager(new[]{ Rule(new App(3)) }, logger: NullLogger.Instance).Initialize();
        Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(IApp,App)>());
    }
}
