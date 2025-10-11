using System.Reactive.Subjects;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Reactive;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

public class MultiArityReactiveConfigTests
{
    private sealed class A { public int Value { get; set; } }
    private sealed class B { public string? Name { get; set; } }
    private sealed class C { public bool Flag { get; set; } }
    private sealed class D { public double Rate { get; set; } }
    private sealed class E { public string? Mode { get; set; } }

    private static ConfigRule RuleFromSubject<T>(BehaviorSubject<T> subject) where T : class
        => TestRules.Observable(subject, required: true);

    [Fact]
    public async Task TwoArity_EmitsOnce_When_OneMemberChanges()
    {
        var subjA = new BehaviorSubject<A>(new() {Value=1});
        var subjB = new BehaviorSubject<B>(new() {Name="x"});
        var rules = new [] { RuleFromSubject(subjA), RuleFromSubject(subjB) };
        using var manager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50).Initialize();

    // Ensure single-type reactive configs are created so per-pass subjects are wired
    manager.GetReactiveConfig<A>();
    manager.GetReactiveConfig<B>();
    var cohort = new ReactiveCohort<A,B>(GetManagerField(manager), NullLogger.Instance, () => manager.GetConfig<A>()!, () => manager.GetConfig<B>()!);
        var emissions = new List<(A,B)>();
        using var sub = cohort.Subscribe(t => emissions.Add(t));

        // change only A
        subjA.OnNext(new() {Value=2});
        await Task.Delay(120); // allow debounce + recompute

        Assert.Single(emissions); // only one pass emitted
        Assert.Equal(2, emissions[0].Item1.Value);
        Assert.Equal("x", emissions[0].Item2.Name);
    }

    [Fact]
    public async Task ThreeArity_SinglePass_MultipleChanges_StillOneEmission()
    {
        var subjA = new BehaviorSubject<A>(new() {Value=1});
        var subjB = new BehaviorSubject<B>(new() {Name="x"});
        var subjC = new BehaviorSubject<C>(new() {Flag=false});
        var rules = new [] { RuleFromSubject(subjA), RuleFromSubject(subjB), RuleFromSubject(subjC) };
        using var manager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50).Initialize();

    manager.GetReactiveConfig<A>();
    manager.GetReactiveConfig<B>();
    manager.GetReactiveConfig<C>();
    var cohort = new ReactiveCohort<A,B,C>(GetManagerField(manager), NullLogger.Instance, () => manager.GetConfig<A>()!, () => manager.GetConfig<B>()!, () => manager.GetConfig<C>()!);
        var emissions = new List<(A,B,C)>();
        using var sub = cohort.Subscribe(t => emissions.Add(t));

        // change all three nearly together (within debounce window)
        subjA.OnNext(new() {Value=10});
        subjB.OnNext(new() {Name="y"});
        subjC.OnNext(new() {Flag=true});
        await Task.Delay(120);

        Assert.Single(emissions);
        var (a,b,c) = emissions[0];
        Assert.Equal(10, a.Value);
        Assert.Equal("y", b.Name);
        Assert.True(c.Flag);
    }

    [Fact]
    public async Task Cohort_DoesNotEmit_When_NoMembersChange()
    {
        var subjA = new BehaviorSubject<A>(new() {Value=1});
        var subjB = new BehaviorSubject<B>(new() {Name="x"});
        var rules = new [] { RuleFromSubject(subjA), RuleFromSubject(subjB) };
        using var manager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50).Initialize();

    manager.GetReactiveConfig<A>();
    manager.GetReactiveConfig<B>();
    var cohort = new ReactiveCohort<A,B>(GetManagerField(manager), NullLogger.Instance, () => manager.GetConfig<A>()!, () => manager.GetConfig<B>()!);
        var emissions = new List<(A,B)>();
        using var sub = cohort.Subscribe(t => emissions.Add(t));

        // Trigger recompute by re-emitting identical objects (no change detection => no emission)
        subjA.OnNext(new() {Value=1});
        subjB.OnNext(new() {Name="x"});
        await Task.Delay(120);

        Assert.Empty(emissions);
    }

    [Fact]
    public async Task FourArity_EmitsOnce_When_AnyChanges()
    {
        var sa = new BehaviorSubject<A>(new() {Value=1});
        var sb = new BehaviorSubject<B>(new() {Name="b1"});
        var sc = new BehaviorSubject<C>(new() {Flag=false});
        var sd = new BehaviorSubject<D>(new() {Rate=1.0});
        var rules = new [] { RuleFromSubject(sa), RuleFromSubject(sb), RuleFromSubject(sc), RuleFromSubject(sd) };
        using var manager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50).Initialize();
        manager.GetReactiveConfig<A>(); manager.GetReactiveConfig<B>(); manager.GetReactiveConfig<C>(); manager.GetReactiveConfig<D>();
        var cohort = new ReactiveCohort<A,B,C,D>(GetManagerField(manager), NullLogger.Instance, () => manager.GetConfig<A>()!, () => manager.GetConfig<B>()!, () => manager.GetConfig<C>()!, () => manager.GetConfig<D>()!);
        var emissions = new List<(A,B,C,D)>();
        using var sub = cohort.Subscribe(x => emissions.Add(x));
        // change one
        sd.OnNext(new() {Rate=2.5});
        await Task.Delay(120);
        Assert.Single(emissions);
        Assert.Equal(2.5, emissions[0].Item4.Rate);
    }

    [Fact]
    public async Task FiveArity_SingleEmission_ForMultipleChangesSamePass()
    {
        var sa = new BehaviorSubject<A>(new() {Value=1});
        var sb = new BehaviorSubject<B>(new() {Name="b1"});
        var sc = new BehaviorSubject<C>(new() {Flag=false});
        var sd = new BehaviorSubject<D>(new() {Rate=1.0});
        var se = new BehaviorSubject<E>(new() {Mode="m1"});
        var rules = new [] { RuleFromSubject(sa), RuleFromSubject(sb), RuleFromSubject(sc), RuleFromSubject(sd), RuleFromSubject(se) };
        using var manager = new ConfigManager(rules, logger: NullLogger.Instance, debounceMilliseconds: 50).Initialize();
        manager.GetReactiveConfig<A>(); manager.GetReactiveConfig<B>(); manager.GetReactiveConfig<C>(); manager.GetReactiveConfig<D>(); manager.GetReactiveConfig<E>();
        var cohort = new ReactiveCohort<A,B,C,D,E>(GetManagerField(manager), NullLogger.Instance, () => manager.GetConfig<A>()!, () => manager.GetConfig<B>()!, () => manager.GetConfig<C>()!, () => manager.GetConfig<D>()!, () => manager.GetConfig<E>()!);
        var emissions = new List<(A,B,C,D,E)>();
        using var sub = cohort.Subscribe(x => emissions.Add(x));
        // multiple changes within debounce window
        sa.OnNext(new() {Value=9});
        sd.OnNext(new() {Rate=3.14});
        se.OnNext(new() {Mode="m2"});
        await Task.Delay(140);
        Assert.Single(emissions);
        var last = emissions[0];
        Assert.Equal(9, last.Item1.Value);
        Assert.Equal(3.14, last.Item4.Rate);
        Assert.Equal("m2", last.Item5.Mode);
    }

    private static ReactiveConfigManager GetManagerField(ConfigManager manager)
    {
        // Reflect private field _reactiveConfigManager
        var field = typeof(ConfigManager).GetField("_reactiveConfigManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (ReactiveConfigManager)field!.GetValue(manager)!;
    }
}
