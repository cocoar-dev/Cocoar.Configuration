using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

/// <summary>
/// Unified tuple-capable IReactiveConfig implementation. Supports any System.ValueTuple shape.
/// Semantics:
///  * Single-type (non-tuple) behaves like original ReactiveConfig<T> (change-only emissions).
///  * Tuple types emit once per recompute pass iff any element changed in that pass, with an atomic aligned snapshot.
/// </summary>
internal sealed class ReactiveTupleConfig<TTuple> : IReactiveConfig<TTuple>, IDisposable where TTuple : struct
{
    private readonly ILogger _logger;
    private readonly IDisposable _subscription; // anchor
    private readonly IObservable<TTuple> _observable; // resilient merged observable
    private readonly Func<object?[], object> _builder; // creates tuple instance
    private readonly Type[] _elementTypes;
    private volatile object?[] _latestValues; // last known element snapshots
    private readonly ConfigManager _configManager;

    public ReactiveTupleConfig(
        ConfigManager configManager,
        ReactiveConfigManager reactiveConfigManager,
        ILogger logger)
    {
        _configManager = configManager;
        _logger = logger;
        (_elementTypes, _builder) = TupleShapeCache.Get(typeof(TTuple));
        if (_elementTypes.Length == 0)
            throw new InvalidOperationException($"{typeof(TTuple).Name} is not a ValueTuple with elements.");

        // Validate existence of each config upfront
        var missing = new List<string>();
        _latestValues = new object?[_elementTypes.Length];
        for (int i = 0; i < _elementTypes.Length; i++)
        {
            var val = configManager.GetConfig(_elementTypes[i]);
            if (val is null)
                missing.Add(_elementTypes[i].Name);
            _latestValues[i] = val; // may be null initially
        }
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Cannot create IReactiveConfig<{typeof(TTuple).Name}>. Missing configuration for: {string.Join(", ", missing)}");

        var merged = CreateMergedPerPassObservable(reactiveConfigManager);
        _observable = merged
            .Catch<TTuple, Exception>(ex =>
            {
                _logger.LogWarning(ex, "Tuple reactive config stream error ignored to keep alive for {TupleType}", typeof(TTuple).Name);
                return Observable.Empty<TTuple>();
            })
            .Retry()
            .Publish()
            .RefCount();

        _subscription = _observable.Subscribe(_ => { }, _ => { }); // anchor keep-hot semantics
    }

    public TTuple CurrentValue
    {
        get
        {
            try
            {
                // Refresh each slot lazily (cheap calls)
                for (int i = 0; i < _elementTypes.Length; i++)
                {
                    var value = _configManager.GetConfig(_elementTypes[i]);
                    if (value != null)
                        _latestValues[i] = value;
                }
                return (TTuple)_builder(_latestValues);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build CurrentValue for tuple {TupleType}", typeof(TTuple).Name);
                return default;
            }
        }
    }

    public IDisposable Subscribe(IObserver<TTuple> observer) => _observable.Subscribe(observer);

    private IObservable<TTuple> CreateMergedPerPassObservable(ReactiveConfigManager manager)
    {
        var streams = new IObservable<object>[_elementTypes.Length];
        for (int i = 0; i < _elementTypes.Length; i++)
        {
            // Use reflection to call generic ObservePerPass<T>
            var method = typeof(ReactiveConfigManager).GetMethod("ObservePerPass", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!;
            var g = method.MakeGenericMethod(_elementTypes[i]);
            var passObservable = g.Invoke(manager, null)!; // IObservable<PassEvent<Ti>>
            // Project to a uniform shape
            var projected = (IObservable<object>)typeof(ReactiveTupleConfig<TTuple>)
                .GetMethod(nameof(Project), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(_elementTypes[i])
                .Invoke(null, new[] { passObservable, i })!;
            streams[i] = projected;
        }

        var merged = streams.Merge();
        return Observable.Create<TTuple>(observer =>
        {
            var gate = new object();
            long currentPassId = -1;
            int received = 0;
            var changedAny = false;
            var slotChanged = new bool[_elementTypes.Length];

            void Reset(long passId)
            {
                currentPassId = passId;
                received = 0;
                changedAny = false;
                Array.Clear(slotChanged, 0, slotChanged.Length);
            }

            Reset(-1);

            return merged.Subscribe(evtObj =>
            {
                if (evtObj is not PassEnvelope env) return; // safety
                lock (gate)
                {
                    if (received == 0)
                    {
                        Reset(env.PassId);
                    }
                    else if (env.PassId != currentPassId)
                    {
                        // Out-of-alignment event (should not happen). Start new pass cautiously.
                        _logger.LogDebug("Tuple pass alignment discrepancy for {TupleType}: got {IncomingPass} expected {CurrentPass}", typeof(TTuple).Name, env.PassId, currentPassId);
                        Reset(env.PassId);
                    }

                    if (!slotChanged[env.Index])
                    {
                        slotChanged[env.Index] = true;
                        received++;
                        if (env.Changed) changedAny = true;
                        _latestValues[env.Index] = env.Value; // update snapshot (even if unchanged to keep reference freshness)
                    }

                    if (received == _elementTypes.Length)
                    {
                        if (changedAny)
                        {
                            try
                            {
                                var tuple = (TTuple)_builder(_latestValues);
                                observer.OnNext(tuple);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed building tuple emission for {TupleType}", typeof(TTuple).Name);
                            }
                        }
                        // ready for next pass
                        currentPassId = -1; // force re-init next event
                    }
                }
            }, ex =>
            {
                observer.OnError(ex); // upstream Catch/Retry wraps this
            }, () =>
            {
                observer.OnCompleted();
            });
        });
    }

    private static IObservable<object> Project<T>(IObservable<ReactiveConfigManager.PassEvent<T>> source, int index)
    {
        return source.Select(e => (object)new PassEnvelope(index, e.PassId, e.Changed, e.Value));
    }

    private readonly record struct PassEnvelope(int Index, long PassId, bool Changed, object? Value);

    public void Dispose() => _subscription.Dispose();
}

/// <summary>
/// Caches flattened element type arrays and compiled tuple builder delegates.
/// Handles nested ValueTuple (Rest) expansion.
/// </summary>
internal static class TupleShapeCache
{
    private static readonly ConcurrentDictionary<Type, (Type[] Elements, Func<object?[], object> Builder)> _cache = new();

    public static (Type[] Elements, Func<object?[], object> Builder) Get(Type tupleType)
    {
        return _cache.GetOrAdd(tupleType, t =>
        {
            var elements = Flatten(t).ToArray();
            var builder = CompileBuilder(t, elements);
            return (elements, builder);
        });
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        if (!IsValueTuple(t)) yield break;
        var fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f.Name == "Rest" && IsValueTuple(f.FieldType))
            {
                foreach (var nested in Flatten(f.FieldType))
                    yield return nested;
            }
            else
            {
                yield return f.FieldType;
            }
        }
    }

    private static bool IsValueTuple(Type t) => t.IsValueType && t.FullName != null && t.FullName.StartsWith("System.ValueTuple");

    private static Func<object?[], object> CompileBuilder(Type tupleType, Type[] elements)
    {
        // Build nested constructor chain according to ValueTuple nesting rules
        // ValueTuple packs first 7 elements then a Rest = ValueTuple<remaining>
        var param = Expression.Parameter(typeof(object?[]), "arr");
        Expression Build(int start)
        {
            int remaining = elements.Length - start;
            if (remaining <= 7)
            {
                var ctorTypes = elements.Skip(start).Take(remaining).ToArray();
                var ctors = GetTupleType(remaining, ctorTypes);
                var args = ctorTypes.Select((t,i) => Expression.Convert(Expression.ArrayIndex(param, Expression.Constant(start + i)), t));
                return Expression.New(ctors.GetConstructors()[0], args);
            }
            else
            {
                var headTypes = elements.Skip(start).Take(7).ToArray();
                var restExpr = Build(start + 7);
                var tupleTypeHead = GetTupleType(8, headTypes.Concat(new[]{ restExpr.Type }).ToArray());
                var args = headTypes.Select((t,i) => Expression.Convert(Expression.ArrayIndex(param, Expression.Constant(start + i)), t))
                    .Concat(new[]{ restExpr });
                return Expression.New(tupleTypeHead.GetConstructors()[0], args);
            }
        }

        var body = Build(0);
        var lambda = Expression.Lambda<Func<object?[], object>>(Expression.Convert(body, typeof(object)), param);
        return lambda.Compile();
    }

    private static Type GetTupleType(int count, Type[] types)
    {
        return count switch
        {
            <= 0 => throw new ArgumentOutOfRangeException(nameof(count)),
            1 => typeof(ValueTuple<>).MakeGenericType(types),
            2 => typeof(ValueTuple<,>).MakeGenericType(types),
            3 => typeof(ValueTuple<,,>).MakeGenericType(types),
            4 => typeof(ValueTuple<,,,>).MakeGenericType(types),
            5 => typeof(ValueTuple<,,,,>).MakeGenericType(types),
            6 => typeof(ValueTuple<,,,,,>).MakeGenericType(types),
            7 => typeof(ValueTuple<,,,,,,>).MakeGenericType(types),
            8 => typeof(ValueTuple<,,,,,,,>).MakeGenericType(types),
            _ => throw new NotSupportedException("Unsupported tuple arity segment")
        };
    }
}
