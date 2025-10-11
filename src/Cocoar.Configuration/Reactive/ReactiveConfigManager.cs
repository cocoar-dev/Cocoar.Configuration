using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reactive.Subjects;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.Configuration.Infrastructure;
using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Reactive;


internal sealed class ReactiveConfigManager(ILogger logger, ExposureRegistry bindingRegistry) : IDisposable
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ExposureRegistry _bindingRegistry = bindingRegistry ?? throw new ArgumentNullException(nameof(bindingRegistry));
    private readonly ConcurrentDictionary<Type, object> _configObservables = new();
    private readonly ConcurrentDictionary<Type, string> _previousConfigHashes = new();

    // Per-pass subjects emit once per recompute pass (even if value unchanged)
    private readonly ConcurrentDictionary<Type, object> _perPassSubjects = new();
    private long _passId;

    private static readonly ConcurrentDictionary<Type, Action<object, object?>> s_onNextCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object?, bool, long, object>> s_passEventFactoryCache = new();


    private static readonly JsonSerializerOptions _optimizedJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        IncludeFields = false
    };


    public IReactiveConfig<T> GetReactiveConfig<T>(Func<T> configAccessor)
    {
        var type = typeof(T);
        
        var subject = (BehaviorSubject<T>)_configObservables.AddOrUpdate(type, 
            _ => CreateBehaviorSubject(configAccessor),
            (_, existing) =>
            {
                if (existing is BehaviorSubject<T> { IsDisposed: false })
                {
                    return existing;
                }
                
                _logger.LogInformation("Recreating dead observable for configuration type {Type}", type);
                return CreateBehaviorSubject(configAccessor);
            });

        _perPassSubjects.GetOrAdd(type, _ => new Subject<PassEvent<T>>());

        return new ReactiveConfig<T>(subject, _logger);
    }


    internal readonly struct PassEvent<T>(T value, bool changed, long passId)
    {
        public T Value { get; } = value;
        public bool Changed { get; } = changed;
        public long PassId { get; } = passId;
    }


    internal IObservable<PassEvent<T>> ObservePerPass<T>()
    {
        if (_perPassSubjects.TryGetValue(typeof(T), out var existing) && existing is Subject<PassEvent<T>> subject)
        {
            return subject.AsObservable();
        }

        var created = (Subject<PassEvent<T>>)_perPassSubjects.GetOrAdd(typeof(T), _ => new Subject<PassEvent<T>>());
        return created.AsObservable();
    }

    private BehaviorSubject<T> CreateBehaviorSubject<T>(Func<T> configAccessor)
    {
        T initialValue;
        try
        {
            initialValue = configAccessor() ?? default(T)!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get initial config for type {Type}, using default value", typeof(T));
            initialValue = default(T)!;
        }

        var initialHash = ComputeConfigHash(initialValue);
        _previousConfigHashes[typeof(T)] = initialHash;

        return new(initialValue);
    }

    public void NotifyConfigurationObservers(Func<Type, object?> configAccessor)
    {
        var passId = Interlocked.Increment(ref _passId);

        var allTypes = new HashSet<Type>();
        foreach (var type in _configObservables.Keys)
        {
            allTypes.Add(type);
        }

        foreach (var type in _perPassSubjects.Keys)
        {
            allTypes.Add(type);
        }

        foreach (var type in allTypes.ToArray())
        {
            var subject = _configObservables.GetValueOrDefault(type);
            object? currentConfig;
            var changed = false;
            try
            {
                currentConfig = configAccessor(type);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get current config for type {Type} during notification, skipping update", type);
                continue;
            }

            try
            {
                var currentHash = ComputeConfigHash(currentConfig);
                var previousHash = _previousConfigHashes.GetValueOrDefault(type, string.Empty);
                changed = currentHash != previousHash || string.IsNullOrEmpty(previousHash);
                if (changed)
                {
                    _previousConfigHashes[type] = currentHash;

                    if (subject is not null)
                    {
                        var payload = PrepareValueForSubject(type, currentConfig);

                        try
                        {
                            PublishToSubject(subject, payload);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed change emission for type {Type}", type);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed change emission for type {Type}", type);
            }

            try
            {
                if (_perPassSubjects.TryGetValue(type, out var perPassObj))
                {
                    var evt = CreatePassEventInstance(type, currentConfig, changed, passId);
                    PublishToSubject(perPassObj, evt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed per-pass emission for type {Type}", type);
            }
        }
    }

    

    private static string ComputeConfigHash(object? config)
    {
        try
        {
            if (config is null)
            {
                return "NULL";
            }

            using var md5 = MD5.Create();
            using var stream = new CryptoStream(Stream.Null, md5, CryptoStreamMode.Write);
            using var writer = new Utf8JsonWriter(stream, new()
            { 
                Indented = false,
                SkipValidation = true 
            });
            
            JsonSerializer.Serialize(writer, config, config.GetType(), _optimizedJsonOptions);
            writer.Flush();
            stream.FlushFinalBlock();
            
            return Convert.ToHexString(md5.Hash!);
        }
        catch
        {
            return $"{config!.GetType().FullName}#{config.GetHashCode()}";
        }
    }

    public void Dispose()
    {
        foreach (var observable in _configObservables.Values.ToArray())
        {
            try
            {
                if (observable is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose configuration observable");
            }
        }

        _configObservables.Clear();
        _previousConfigHashes.Clear();
        foreach (var perPass in _perPassSubjects.Values.ToArray())
        {
            Safety.DisposeQuietly(perPass as IDisposable);
        }
        _perPassSubjects.Clear();
    }

    private static void PublishToSubject(object subject, object? value)
    {
        var action = s_onNextCache.GetOrAdd(subject.GetType(), CreatePublishDelegate);
        action(subject, value);
    }

    private static Action<object, object?> CreatePublishDelegate(Type subjectType)
    {
        var onNext = subjectType.GetMethods()
            .FirstOrDefault(m => string.Equals(m.Name, "OnNext", StringComparison.Ordinal) && m.GetParameters().Length == 1);
        if (onNext == null)
        {
            return (_, _) => { };
        }

        var subjectParameter = Expression.Parameter(typeof(object), "subject");
        var valueParameter = Expression.Parameter(typeof(object), "value");

        var castSubject = Expression.Convert(subjectParameter, subjectType);
        var parameterType = onNext.GetParameters()[0].ParameterType;
        var castValue = Expression.Convert(valueParameter, parameterType);

        var body = Expression.Call(castSubject, onNext, castValue);
        var lambda = Expression.Lambda<Action<object, object?>>(body, subjectParameter, valueParameter);
        return lambda.Compile();
    }

    private static object CreatePassEventInstance(Type configType, object? value, bool changed, long passId)
    {
        var factory = s_passEventFactoryCache.GetOrAdd(configType, CreatePassEventFactory);
        return factory(value, changed, passId);
    }

    private static Func<object?, bool, long, object> CreatePassEventFactory(Type configType)
    {
        var passEventType = typeof(PassEvent<>).MakeGenericType(configType);
        var ctor = passEventType.GetConstructors()
            .FirstOrDefault(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length == 3 &&
                       parameters[0].ParameterType == configType &&
                       parameters[1].ParameterType == typeof(bool) &&
                       parameters[2].ParameterType == typeof(long);
            })
            ?? throw new InvalidOperationException($"Unable to locate PassEvent constructor for type {configType}");

        var valueParam = Expression.Parameter(typeof(object), "value");
        var changedParam = Expression.Parameter(typeof(bool), "changed");
        var passIdParam = Expression.Parameter(typeof(long), "passId");

        Expression typedValue = configType.IsValueType
            ? Expression.Condition(
                Expression.Equal(valueParam, Expression.Constant(null)),
                Expression.Default(configType),
                Expression.Convert(valueParam, configType))
            : Expression.Convert(valueParam, configType);

        var newExpression = Expression.New(ctor, typedValue, changedParam, passIdParam);
        var body = Expression.Convert(newExpression, typeof(object));

        return Expression.Lambda<Func<object?, bool, long, object>>(body, valueParam, changedParam, passIdParam).Compile();
    }

    private object? PrepareValueForSubject(Type requestedType, object? currentValue)
    {
        if (currentValue is null)
        {
            return null;
        }

        if (requestedType.IsInstanceOfType(currentValue))
        {
            return currentValue;
        }

        if (_bindingRegistry.TryGetConcreteType(requestedType, out var concreteType) &&
            concreteType.IsInstanceOfType(currentValue))
        {
            return currentValue;
        }

        return currentValue;
    }
}
