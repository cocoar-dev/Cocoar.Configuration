using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

/// <summary>
/// Manages reactive configuration observables, change detection, and notifications.
/// Handles the creation, tracking, and updating of BehaviorSubjects for configuration changes.
/// </summary>
internal sealed class ReactiveConfigManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly BindingRegistry _bindingRegistry;
    private readonly ConcurrentDictionary<Type, object> _configObservables = new();
    private readonly ConcurrentDictionary<Type, string> _previousConfigHashes = new();
    private readonly object _observableLock = new();

    // Per-pass subjects emit once per recompute pass (even if value unchanged)
    private readonly ConcurrentDictionary<Type, object> _perPassSubjects = new();
    private long _passId; // monotonically increasing recompute pass id

    /// <summary>
    /// Optimized JSON serialization options for hashing performance.
    /// Reduces serialization overhead by disabling unnecessary formatting and features.
    /// </summary>
    private static readonly JsonSerializerOptions _optimizedJsonOptions = new()
    {
        WriteIndented = false,                                              // No pretty printing
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,      // Skip nulls
        PropertyNamingPolicy = null,                                        // No naming conversion
        IncludeFields = false                                              // Properties only
    };

    public ReactiveConfigManager(ILogger logger, BindingRegistry bindingRegistry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bindingRegistry = bindingRegistry ?? throw new ArgumentNullException(nameof(bindingRegistry));
    }

    /// <summary>
    /// Gets or creates a reactive configuration that provides both observable updates and current value access.
    /// Perfect for dependency injection scenarios where you need both reactive updates and immediate value access.
    /// The returned observable is error-resilient and will never terminate due to subscriber errors.
    /// </summary>
    /// <typeparam name="T">The configuration type to observe</typeparam>
    /// <param name="configAccessor">Function to get the current configuration value</param>
    /// <returns>A reactive configuration that emits configuration values and provides current value access</returns>
    public IReactiveConfig<T> GetReactiveConfig<T>(Func<T> configAccessor)
    {
        var type = typeof(T);
        
        // Get or create the subject for this type, with recovery for dead observables
        var subject = (BehaviorSubject<T>)_configObservables.AddOrUpdate(type, 
            // Factory for new entry
            _ => CreateBehaviorSubject(configAccessor),
            // Update function - recreate if the existing subject is dead
            (_, existing) =>
            {
                if (existing is BehaviorSubject<T> behaviorSubject && !behaviorSubject.IsDisposed)
                {
                    return existing; // Keep alive subject
                }
                
                // Subject is dead/disposed, create a new one
                _logger.LogInformation("Recreating dead observable for configuration type {Type}", type);
                return CreateBehaviorSubject(configAccessor);
            });

        // Return error-resilient reactive config wrapper
        // Ensure per-pass subject exists for this type (lazy creation)
        _perPassSubjects.GetOrAdd(type, _ => new Subject<PassEvent<T>>());

        return new ReactiveConfig<T>(subject, _logger);
    }

    /// <summary>
    /// Internal per-pass event used to align multi-arity reactive configs on recompute boundaries.
    /// Always emitted once per pass for each observed type, regardless of whether the value changed.
    /// </summary>
    internal readonly struct PassEvent<T>
    {
        public PassEvent(T value, bool changed, long passId)
        {
            Value = value;
            Changed = changed;
            PassId = passId;
        }
        public T Value { get; }
        public bool Changed { get; }
        public long PassId { get; }
    }

    /// <summary>
    /// Gets the per-pass observable for a type. Always emits once per recompute pass with PassEvent metadata.
    /// </summary>
    internal IObservable<PassEvent<T>> ObservePerPass<T>()
    {
        if (_perPassSubjects.TryGetValue(typeof(T), out var existing) && existing is Subject<PassEvent<T>> subject)
            return subject.AsObservable();

        var created = (Subject<PassEvent<T>>)_perPassSubjects.GetOrAdd(typeof(T), _ => new Subject<PassEvent<T>>());
        return created.AsObservable();
    }

    /// <summary>
    /// Creates a new BehaviorSubject with error handling for initial value.
    /// </summary>
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

        // Store the initial value hash to prevent duplicate emission during first notification
        var initialHash = ComputeConfigHash(initialValue);
        _previousConfigHashes[typeof(T)] = initialHash;

        return new BehaviorSubject<T>(initialValue);
    }

    /// <summary>
    /// Notifies all configuration observers of potential changes by emitting current values.
    /// This is called after recomputation to ensure observers get updated configurations.
    /// Individual observer failures are isolated and logged without affecting other observers.
    /// Only emits updates if the configuration value has actually changed using hash comparison.
    /// </summary>
    /// <param name="configAccessor">Function to get current configuration values by type</param>
    public void NotifyConfigurationObservers(Func<Type, object?> configAccessor)
    {
        var passId = Interlocked.Increment(ref _passId);

        foreach (var kvp in _configObservables.ToArray()) // snapshot to avoid enumeration issues
        {
            var type = kvp.Key;
            var subject = kvp.Value;
            object? currentConfig;
            bool changed = false;
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
                // Change detection against previous hash (as before)
                var currentHash = ComputeConfigHash(currentConfig);
                var previousHash = _previousConfigHashes.GetValueOrDefault(type, string.Empty);
                changed = currentHash != previousHash || string.IsNullOrEmpty(previousHash);
                if (changed)
                {
                    _previousConfigHashes[type] = currentHash;
                    // Emit to behavior subject only when changed (preserve existing semantics)
                    if (subject is ISubject<object?> objectSubject)
                    {
                        objectSubject.OnNext(currentConfig);
                    }
                    else
                    {
                        var onNextMethod = subject.GetType().GetMethod("OnNext");
                        if (onNextMethod != null)
                        {
                            var convertedValue = currentConfig;
                            if (currentConfig != null && !type.IsAssignableFrom(currentConfig.GetType()))
                            {
                                if (_bindingRegistry.TryGetConcreteType(type, out var concreteType) &&
                                    concreteType.IsAssignableFrom(currentConfig.GetType()))
                                {
                                    convertedValue = currentConfig;
                                }
                            }
                            onNextMethod.Invoke(subject, new[] { convertedValue });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed change emission for type {Type}", type);
            }

            // Always emit per-pass event (even if not changed)
            try
            {
                if (_perPassSubjects.TryGetValue(type, out var perPassObj))
                {
                    var passEventType = typeof(PassEvent<>).MakeGenericType(type);
                    // Activator.CreateInstance arguments must match ctor: (value, changed, passId)
                    var evt = Activator.CreateInstance(passEventType, new[] { currentConfig!, changed, passId })!;
                    var onNextMethod = perPassObj.GetType().GetMethod("OnNext");
                    onNextMethod?.Invoke(perPassObj, new[] { evt });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed per-pass emission for type {Type}", type);
            }
        }
    }

    
    /// <summary>
    /// Computes an MD5 hash of the configuration object for change detection.
    /// Uses streaming serialization directly to hash for optimal performance - no intermediate string allocation.
    /// </summary>
    private static string ComputeConfigHash(object? config)
    {
        try
        {
            if (config is null) return "NULL";
            
            using var md5 = MD5.Create();
            using var stream = new CryptoStream(Stream.Null, md5, CryptoStreamMode.Write);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions 
            { 
                Indented = false,
                SkipValidation = true  // Skip validation for performance
            });
            
            JsonSerializer.Serialize(writer, config, config.GetType(), _optimizedJsonOptions);
            writer.Flush();
            stream.FlushFinalBlock();
            
            return Convert.ToHexString(md5.Hash!);
        }
        catch
        {
            // Fallback for non-serializable objects - use type and reference hash
            return $"{config!.GetType().FullName}#{config.GetHashCode()}";
        }
    }

    public void Dispose()
    {
        // Dispose all active BehaviorSubjects
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
            try { (perPass as IDisposable)?.Dispose(); } catch { /* ignore */ }
        }
        _perPassSubjects.Clear();
    }
}
