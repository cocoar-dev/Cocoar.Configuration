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
        var mgr = ConfigManager.Create(c => c.WithConfiguration(new[]{
            CreateStaticRule(new App(1))
        }).UseLogger(NullLogger.Instance));
        // int is not a configured type; guard should throw
        Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(App,int)>());
    }

    [Fact]
    public void Tuple_With_Exposed_Interface_Succeeds()
    {
        var mgr = ConfigManager.Create(c => c.WithConfiguration(new[]{
            CreateStaticRule(new App(2))
        }, setup => [setup.ConcreteType<App>().ExposeAs<IApp>()]).UseLogger(NullLogger.Instance));
        var cfg = mgr.GetReactiveConfig<(IApp,App)>();
        var snapshot = cfg.CurrentValue;
        Assert.Equal(2, snapshot.Item1.V);
        Assert.Equal(2, snapshot.Item2.V);
    }

    [Fact]
    public void Tuple_With_Unexposed_Interface_Fails()
    {
        var mgr = ConfigManager.Create(c => c.WithConfiguration(new[]{
            CreateStaticRule(new App(3))
        }).UseLogger(NullLogger.Instance));
        Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(IApp,App)>());
    }
}
