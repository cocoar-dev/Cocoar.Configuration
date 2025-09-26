using System.Reactive.Linq;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Reactive;


public interface IReactiveConfig<T1, T2> : IObservable<(T1, T2)>
{
    (T1, T2) Current { get; }
}

public interface IReactiveConfig<T1, T2, T3> : IObservable<(T1, T2, T3)>
{
    (T1, T2, T3) Current { get; }
}

public interface IReactiveConfig<T1, T2, T3, T4> : IObservable<(T1, T2, T3, T4)>
{
    (T1, T2, T3, T4) Current { get; }
}

public interface IReactiveConfig<T1, T2, T3, T4, T5> : IObservable<(T1, T2, T3, T4, T5)>
{
    (T1, T2, T3, T4, T5) Current { get; }
}

internal sealed class ReactiveCohort<T1, T2> : IReactiveConfig<T1, T2>, IDisposable
{
    private readonly IObservable<(T1, T2)> _changes;
    private readonly Func<T1> _get1;
    private readonly Func<T2> _get2;
    private readonly IDisposable _anchor;

    public ReactiveCohort(ReactiveConfigManager manager, ILogger logger, Func<T1> get1, Func<T2> get2)
    {
        _get1 = get1; _get2 = get2;
        var s1 = manager.ObservePerPass<T1>();
        var s2 = manager.ObservePerPass<T2>();

        _changes = s1.Zip(s2, (e1, e2) => (e1, e2))
            .Where(t => t.e1.PassId == t.e2.PassId)
            .Where(t => t.e1.Changed || t.e2.Changed)
            .Select(t => (t.e1.Value, t.e2.Value))
            .Catch<(T1, T2), Exception>(ex =>
            {
                logger.LogWarning(ex, "Cohort<T1,T2> stream error ignored to keep alive");
                return Observable.Empty<(T1, T2)>();
            })
            .Retry()
            .Publish()
            .RefCount();

        _anchor = _changes.Subscribe(_ => { }, _ => { });
    }

    public (T1, T2) Current => (_get1(), _get2());
    public IDisposable Subscribe(IObserver<(T1, T2)> observer) => _changes.Subscribe(observer);
    public void Dispose() => _anchor.Dispose();
}

internal sealed class ReactiveCohort<T1, T2, T3> : IReactiveConfig<T1, T2, T3>, IDisposable
{
    private readonly IObservable<(T1, T2, T3)> _changes;
    private readonly Func<T1> _get1; private readonly Func<T2> _get2; private readonly Func<T3> _get3;
    private readonly IDisposable _anchor;

    public ReactiveCohort(ReactiveConfigManager manager, ILogger logger, Func<T1> g1, Func<T2> g2, Func<T3> g3)
    {
        _get1 = g1; _get2 = g2; _get3 = g3;
        var s1 = manager.ObservePerPass<T1>();
        var s2 = manager.ObservePerPass<T2>();
        var s3 = manager.ObservePerPass<T3>();

        _changes = s1.Zip(s2, (a, b) => (a, b))
            .Zip(s3, (ab, c) => (ab.a, ab.b, c))
            .Where(t => t.a.PassId == t.b.PassId && t.a.PassId == t.c.PassId)
            .Where(t => t.a.Changed || t.b.Changed || t.c.Changed)
            .Select(t => (t.a.Value, t.b.Value, t.c.Value))
            .Catch<(T1, T2, T3), Exception>(ex =>
            {
                logger.LogWarning(ex, "Cohort<T1,T2,T3> stream error ignored to keep alive");
                return Observable.Empty<(T1, T2, T3)>();
            })
            .Retry()
            .Publish()
            .RefCount();

        _anchor = _changes.Subscribe(_ => { }, _ => { });
    }

    public (T1, T2, T3) Current => (_get1(), _get2(), _get3());
    public IDisposable Subscribe(IObserver<(T1, T2, T3)> observer) => _changes.Subscribe(observer);
    public void Dispose() => _anchor.Dispose();
}

internal sealed class ReactiveCohort<T1, T2, T3, T4> : IReactiveConfig<T1, T2, T3, T4>, IDisposable
{
    private readonly IObservable<(T1, T2, T3, T4)> _changes;
    private readonly Func<T1> _g1; private readonly Func<T2> _g2; private readonly Func<T3> _g3; private readonly Func<T4> _g4;
    private readonly IDisposable _anchor;

    public ReactiveCohort(ReactiveConfigManager manager, ILogger logger, Func<T1> g1, Func<T2> g2, Func<T3> g3, Func<T4> g4)
    {
        _g1 = g1; _g2 = g2; _g3 = g3; _g4 = g4;
        var s1 = manager.ObservePerPass<T1>();
        var s2 = manager.ObservePerPass<T2>();
        var s3 = manager.ObservePerPass<T3>();
        var s4 = manager.ObservePerPass<T4>();

        _changes = s1.Zip(s2, (a,b) => (a,b))
            .Zip(s3, (ab,c) => (ab.a, ab.b, c))
            .Zip(s4, (abc,d) => (abc.a, abc.b, abc.c, d))
            .Where(t => t.a.PassId == t.b.PassId && t.a.PassId == t.c.PassId && t.a.PassId == t.d.PassId)
            .Where(t => t.a.Changed || t.b.Changed || t.c.Changed || t.d.Changed)
            .Select(t => (t.a.Value, t.b.Value, t.c.Value, t.d.Value))
            .Catch<(T1, T2, T3, T4), Exception>(ex =>
            {
                logger.LogWarning(ex, "Cohort<T1..T4> stream error ignored to keep alive");
                return Observable.Empty<(T1, T2, T3, T4)>();
            })
            .Retry()
            .Publish()
            .RefCount();

        _anchor = _changes.Subscribe(_ => { }, _ => { });
    }

    public (T1, T2, T3, T4) Current => (_g1(), _g2(), _g3(), _g4());
    public IDisposable Subscribe(IObserver<(T1, T2, T3, T4)> o) => _changes.Subscribe(o);
    public void Dispose() => _anchor.Dispose();
}

internal sealed class ReactiveCohort<T1, T2, T3, T4, T5> : IReactiveConfig<T1, T2, T3, T4, T5>, IDisposable
{
    private readonly IObservable<(T1, T2, T3, T4, T5)> _changes;
    private readonly Func<T1> _g1; private readonly Func<T2> _g2; private readonly Func<T3> _g3; private readonly Func<T4> _g4; private readonly Func<T5> _g5;
    private readonly IDisposable _anchor;

    public ReactiveCohort(ReactiveConfigManager manager, ILogger logger, Func<T1> g1, Func<T2> g2, Func<T3> g3, Func<T4> g4, Func<T5> g5)
    {
        _g1 = g1; _g2 = g2; _g3 = g3; _g4 = g4; _g5 = g5;
        var s1 = manager.ObservePerPass<T1>();
        var s2 = manager.ObservePerPass<T2>();
        var s3 = manager.ObservePerPass<T3>();
        var s4 = manager.ObservePerPass<T4>();
        var s5 = manager.ObservePerPass<T5>();

        _changes = s1.Zip(s2, (a,b) => (a,b))
            .Zip(s3, (ab,c) => (ab.a, ab.b, c))
            .Zip(s4, (abc,d) => (abc.a, abc.b, abc.c, d))
            .Zip(s5, (abcd,e) => (abcd.a, abcd.b, abcd.c, abcd.d, e))
            .Where(t => t.a.PassId == t.b.PassId && t.a.PassId == t.c.PassId && t.a.PassId == t.d.PassId && t.a.PassId == t.e.PassId)
            .Where(t => t.a.Changed || t.b.Changed || t.c.Changed || t.d.Changed || t.e.Changed)
            .Select(t => (t.a.Value, t.b.Value, t.c.Value, t.d.Value, t.e.Value))
            .Catch<(T1, T2, T3, T4, T5), Exception>(ex =>
            {
                logger.LogWarning(ex, "Cohort<T1..T5> stream error ignored to keep alive");
                return Observable.Empty<(T1, T2, T3, T4, T5)>();
            })
            .Retry()
            .Publish()
            .RefCount();

        _anchor = _changes.Subscribe(_ => { }, _ => { });
    }

    public (T1, T2, T3, T4, T5) Current => (_g1(), _g2(), _g3(), _g4(), _g5());
    public IDisposable Subscribe(IObserver<(T1, T2, T3, T4, T5)> o) => _changes.Subscribe(o);
    public void Dispose() => _anchor.Dispose();
}
