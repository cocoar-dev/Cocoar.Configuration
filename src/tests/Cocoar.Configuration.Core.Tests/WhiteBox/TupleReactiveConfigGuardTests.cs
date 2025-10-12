using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration;
using Cocoar.Configuration.Core.Tests.Helpers;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

public class TupleReactiveConfigGuardTests
{
    private interface IApp { int V { get; } }
    private record App(int V) : IApp;

    private static ConfigRule CreateStaticRule<T>(T value)
    {
        var rulesBuilder = new RulesBuilder();
        return rulesBuilder.For<T>().FromStaticJson(System.Text.Json.JsonSerializer.Serialize(value)).Required();
    }

    [Fact]
    public void Tuple_With_Unconfigured_Primitive_Fails()
    {
        var mgr = new ConfigManager(new[]{ 
            CreateStaticRule(new App(1))
        }, logger: NullLogger.Instance).Initialize();
        // int is not a configured type; guard should throw
        Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(App,int)>());
    }

    [Fact]
    public void Tuple_With_Exposed_Interface_Succeeds()
    {
        var mgr = new ConfigManager(new[]{ 
            CreateStaticRule(new App(2))
        }, c => [c.ConcreteType<App>().ExposeAs<IApp>()], NullLogger.Instance).Initialize();
        var cfg = mgr.GetReactiveConfig<(IApp,App)>();
        var snapshot = cfg.CurrentValue;
        Assert.Equal(2, snapshot.Item1.V);
        Assert.Equal(2, snapshot.Item2.V);
    }

    [Fact]
    public void Tuple_With_Unexposed_Interface_Fails()
    {
        var mgr = new ConfigManager(new[]{ 
            CreateStaticRule(new App(3))
        }, logger: NullLogger.Instance).Initialize();
        Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(IApp,App)>());
    }
}
