using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Reactive;

internal static partial class ReactiveTupleConfigLog
{
    [LoggerMessage(EventId = 6100, Level = LogLevel.Warning, Message = "Tuple reactive config stream error ignored to keep alive for {TupleType}")]
    public static partial void TupleStreamErrorIgnored(this ILogger logger, Exception exception, string TupleType);

    [LoggerMessage(EventId = 6101, Level = LogLevel.Warning, Message = "Failed to build CurrentValue for tuple {TupleType}")]
    public static partial void BuildCurrentValueFailed(this ILogger logger, Exception exception, string TupleType);

    [LoggerMessage(EventId = 6103, Level = LogLevel.Warning, Message = "Failed building tuple emission for {TupleType}")]
    public static partial void BuildTupleEmissionFailed(this ILogger logger, Exception exception, string TupleType);
}

/// <summary>
/// Provides reactive tuple configuration using the MasterBackplane.
/// Tuple updates are atomic - all elements update together when the snapshot changes.
/// </summary>
internal sealed class ReactiveTupleConfig<TTuple> : IReactiveConfig<TTuple>, IDisposable where TTuple : struct
{
    private readonly ILogger _logger;
    private readonly IDisposable _subscription;
    private readonly IObservable<TTuple> _observable;
    private readonly Func<object?[], object> _builder;
    private readonly Type[] _elementTypes;
    private readonly ConfigManager _configManager;
    private readonly Infrastructure.ExposureRegistry? _bindingRegistry;

    public ReactiveTupleConfig(
        ConfigManager configManager,
        ReactiveConfigManager reactiveConfigManager,
        ILogger logger,
        Infrastructure.ExposureRegistry? bindingRegistry = null)
    {
        _configManager = configManager;
        _logger = logger;
        _bindingRegistry = bindingRegistry;
        (_elementTypes, _builder) = TupleShapeCache.Get(typeof(TTuple));
        if (_elementTypes.Length == 0)
        {
            throw new InvalidOperationException($"{typeof(TTuple).Name} is not a ValueTuple with elements.");
        }

        // Validate all elements are present
        var missing = new List<string>();
        for (var i = 0; i < _elementTypes.Length; i++)
        {
            var val = configManager.GetConfig(_elementTypes[i]);
            if (val is null)
            {
                missing.Add(_elementTypes[i].Name);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot create IReactiveConfig<{typeof(TTuple).Name}>. Missing configuration for: {string.Join(", ", missing)}");
        }

        // Create observable from the backplane's snapshot stream
        // This provides atomicity - all tuple elements update together
        _observable = CreateTupleObservable(configManager)
            .Catch<TTuple, Exception>(ex =>
            {
                _logger.TupleStreamErrorIgnored(ex, typeof(TTuple).Name);
                return Observable.Empty<TTuple>();
            })
            .Retry()
            .Publish()
            .RefCount();

        _subscription = _observable.Subscribe(_ => { }, _ => { });
    }

    public TTuple CurrentValue
    {
        get
        {
            try
            {
                var values = new object?[_elementTypes.Length];
                for (var i = 0; i < _elementTypes.Length; i++)
                {
                    values[i] = _configManager.GetConfig(_elementTypes[i]);
                }
                return (TTuple)_builder(values);
            }
            catch (Exception ex)
            {
                _logger.BuildCurrentValueFailed(ex, typeof(TTuple).Name);
                return default;
            }
        }
    }

    public IDisposable Subscribe(IObserver<TTuple> observer) => _observable.Subscribe(observer);

    private IObservable<TTuple> CreateTupleObservable(ConfigManager configManager)
    {
        // Access the backplane through the state (after initialization)
        // The backplane provides atomic updates for all types
        return Observable.Create<TTuple>(observer =>
        {
            TTuple? previousTuple = null;

            // Subscribe to the backplane's snapshot stream
            var backplane = configManager.GetType()
                .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(configManager);

            if (backplane == null)
            {
                observer.OnError(new InvalidOperationException("Cannot access ConfigurationState for tuple observation"));
                return Disposable.Empty;
            }

            var backplaneProp = backplane.GetType().GetProperty("Backplane", BindingFlags.Public | BindingFlags.Instance);
            var masterBackplane = backplaneProp?.GetValue(backplane);

            if (masterBackplane == null)
            {
                observer.OnError(new InvalidOperationException("MasterBackplane not initialized for tuple observation"));
                return Disposable.Empty;
            }

            var snapshotStreamProp = masterBackplane.GetType().GetProperty("SnapshotStream");
            var snapshotStream = snapshotStreamProp?.GetValue(masterBackplane) as IObservable<ConfigSnapshot>;

            if (snapshotStream == null)
            {
                observer.OnError(new InvalidOperationException("Cannot access SnapshotStream for tuple observation"));
                return Disposable.Empty;
            }

            return snapshotStream.Subscribe(snapshot =>
            {
                try
                {
                    var values = new object?[_elementTypes.Length];
                    var allPresent = true;

                    for (var i = 0; i < _elementTypes.Length; i++)
                    {
                        values[i] = GetConfigFromSnapshot(snapshot, _elementTypes[i]);
                        if (values[i] == null)
                        {
                            allPresent = false;
                            break;
                        }
                    }

                    if (!allPresent)
                    {
                        return; // Skip if any element is missing
                    }

                    var tuple = (TTuple)_builder(values);

                    // Only emit if any element changed (using reference equality)
                    var changed = previousTuple == null;
                    if (!changed && previousTuple.HasValue)
                    {
                        var prevValues = ExtractTupleValues(previousTuple.Value);
                        for (var i = 0; i < _elementTypes.Length; i++)
                        {
                            if (!ReferenceEquals(prevValues[i], values[i]))
                            {
                                changed = true;
                                break;
                            }
                        }
                    }

                    if (changed)
                    {
                        previousTuple = tuple;
                        observer.OnNext(tuple);
                    }
                }
                catch (Exception ex)
                {
                    _logger.BuildTupleEmissionFailed(ex, typeof(TTuple).Name);
                }
            }, observer.OnError, observer.OnCompleted);
        });
    }

    /// <summary>
    /// Gets a config from the snapshot, handling interface-to-concrete type resolution.
    /// </summary>
    private object? GetConfigFromSnapshot(ConfigSnapshot snapshot, Type type)
    {
        // Try direct lookup first
        var result = snapshot.GetConfig(type);
        if (result != null)
        {
            return result;
        }

        // Try interface-to-concrete mapping
        if (type.IsInterface && _bindingRegistry != null &&
            _bindingRegistry.TryGetConcreteType(type, out var concreteType))
        {
            return snapshot.GetConfig(concreteType);
        }

        return null;
    }

    private object?[] ExtractTupleValues(TTuple tuple)
    {
        var values = new object?[_elementTypes.Length];
        var fields = typeof(TTuple).GetFields(BindingFlags.Public | BindingFlags.Instance);

        var index = 0;
        ExtractRecursive(tuple, fields, ref index, values);
        return values;
    }

    private static void ExtractRecursive(object tuple, FieldInfo[] fields, ref int index, object?[] values)
    {
        foreach (var field in fields)
        {
            if (field.Name == "Rest" && field.FieldType.FullName?.StartsWith("System.ValueTuple", StringComparison.Ordinal) == true)
            {
                var rest = field.GetValue(tuple);
                if (rest != null)
                {
                    var restFields = field.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                    ExtractRecursive(rest, restFields, ref index, values);
                }
            }
            else
            {
                values[index++] = field.GetValue(tuple);
            }
        }
    }

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
            var builder = CompileBuilder(elements);
            return (elements, builder);
        });
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        if (!IsValueTuple(t))
        {
            yield break;
        }

        var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (f.Name == "Rest" && IsValueTuple(f.FieldType))
            {
                foreach (var nested in Flatten(f.FieldType))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return f.FieldType;
            }
        }
    }

    private static bool IsValueTuple(Type t) => t is { IsValueType: true, FullName: not null } && t.FullName.StartsWith("System.ValueTuple", StringComparison.Ordinal);

    private static Func<object?[], object> CompileBuilder(Type[] elements)
    {
        var param = Expression.Parameter(typeof(object?[]), "arr");

        var body = Build(0);
        var lambda = Expression.Lambda<Func<object?[], object>>(Expression.Convert(body, typeof(object)), param);
        return lambda.Compile();

        Expression Build(int start)
        {
            var remaining = elements.Length - start;
            if (remaining <= 7)
            {
                var ctorTypes = elements.Skip(start).Take(remaining).ToArray();
                var ctors = GetTupleType(remaining, ctorTypes);
                var args = ctorTypes.Select((t, i) => Expression.Convert(Expression.ArrayIndex(param, Expression.Constant(start + i)), t));
                return Expression.New(ctors.GetConstructors()[0], args);
            }
            else
            {
                var headTypes = elements.Skip(start).Take(7).ToArray();
                var restExpr = Build(start + 7);
                var tupleTypeHead = GetTupleType(8, headTypes.Concat([restExpr.Type]).ToArray());
                var args = headTypes.Select((t, i) => Expression.Convert(Expression.ArrayIndex(param, Expression.Constant(start + i)), t))
                    .Concat([restExpr]);
                return Expression.New(tupleTypeHead.GetConstructors()[0], args);
            }
        }
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
