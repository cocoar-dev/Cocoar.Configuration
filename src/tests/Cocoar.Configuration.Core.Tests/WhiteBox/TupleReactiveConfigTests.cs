using Cocoar.Configuration.Rules;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

public class TupleReactiveConfigTests
{
    // Simple POCO configs
    private record A(int V);
    private record B(string S);
    private record C(bool Flag);
    private record D(double X);
    private record E(int Z);
    private record F(Guid Id);
    private record G(DateTime T);
    private record H(long L);

    private static ConfigManager Create(params ConfigRule[] rules)
    {
        var mgr = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 10).Initialize();
        return mgr;
    }

    private static (ConfigRule Rule, System.Reactive.Subjects.BehaviorSubject<T> Subject) Rule<T>(T value)
    {
        var subject = new System.Reactive.Subjects.BehaviorSubject<T>(value);
        var rule = ConfigRule.Create<global::Cocoar.Configuration.Providers.ObservableProvider<T>, global::Cocoar.Configuration.Providers.ObservableProviderOptions<T>, global::Cocoar.Configuration.Providers.ObservableProviderQuery>(
            _ => new(subject),
            _ => global::Cocoar.Configuration.Providers.ObservableProviderQuery.Default,
            typeof(T),
            new() { Required = true });
        return (rule, subject);
    }

    [Fact]
    public async Task Tuple2_Emits_WhenEitherElementChanges()
    {
    var (ruleA, subjA) = Rule(new A(1));
    var (ruleB, subjB) = Rule(new B("x"));
    var mgr = Create(ruleA, ruleB);
        var reactive = mgr.GetReactiveConfig<(A,B)>();
        var emitted = new List<(A,B)>();
        using var sub = reactive.Subscribe(v => emitted.Add(v));
        subjA.OnNext(new(2));
        await Task.Delay(180); // debounce + recompute
        Assert.Contains(emitted, t => t.Item1.V == 2 && t.Item2.S == "x");
    }

    [Fact]
    public async Task Tuple5_Emits_SinglePass_WhenMultipleChange()
    {
        var rules = new List<ConfigRule>();
    var (r1, s1) = Rule(new A(1)); rules.Add(r1);
    var (r2, s2) = Rule(new B("a")); rules.Add(r2);
    var (r3, s3) = Rule(new C(true)); rules.Add(r3);
    var (r4, s4) = Rule(new D(1.0)); rules.Add(r4);
    var (r5, s5) = Rule(new E(5)); rules.Add(r5);
        var mgr = Create(rules.ToArray());
        var reactive = mgr.GetReactiveConfig<(A,B,C,D,E)>();
        var list = new List<(A,B,C,D,E)>();
        using var sub = reactive.Subscribe(v => list.Add(v));
        s1.OnNext(new(9));
        s3.OnNext(new(false));
        s5.OnNext(new(42));
        await Task.Delay(220);
        var matching = list.Where(v => v.Item1.V==9 && v.Item3.Flag==false && v.Item5.Z==42).ToList();
        Assert.Equal(1, matching.Count);
    }

    [Fact]
    public async Task LargeTuple8_Supported_WithNestedRest()
    {
        var rules = new List<ConfigRule>();
    var (r1, s1) = Rule(new A(1)); rules.Add(r1);
    var (r2, s2) = Rule(new B("b")); rules.Add(r2);
    var (r3, s3) = Rule(new C(true)); rules.Add(r3);
    var (r4, s4) = Rule(new D(2.5)); rules.Add(r4);
    var (r5, s5) = Rule(new E(7)); rules.Add(r5);
    var (r6, s6) = Rule(new F(Guid.NewGuid())); rules.Add(r6);
    var (r7, s7) = Rule(new G(DateTime.UtcNow)); rules.Add(r7);
    var (r8, s8) = Rule(new H(1234)); rules.Add(r8);
        var mgr = Create(rules.ToArray());
        var reactive = mgr.GetReactiveConfig<(A,B,C,D,E,F,G,H)>();
        var emissions = new List<(A,B,C,D,E,F,G,H)>();
        using var sub = reactive.Subscribe(e => emissions.Add(e));
        s8.OnNext(new(9999));
        await Task.Delay(180);
        Assert.Contains(emissions, t => t.Item8.L == 9999);
    }

    [Fact]
    public void MissingConfig_Throws()
    {
    var (only, _) = Rule(new A(1));
    var mgr = Create(only);
        Assert.Throws<InvalidOperationException>(() => mgr.GetReactiveConfig<(A,B)>());
    }
}
