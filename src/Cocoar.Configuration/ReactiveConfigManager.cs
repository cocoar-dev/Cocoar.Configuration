using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Reflection;
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
        return new ReactiveConfig<T>(subject, _logger);
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
        foreach (var kvp in _configObservables.ToArray()) // ToArray to avoid collection modification issues
        {
            try
            {
                var type = kvp.Key;
                var subject = kvp.Value;
                
                // Safely get the current configuration value for this type
                object? currentConfig;
                try
                {
                    currentConfig = configAccessor(type);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get current config for type {Type} during notification, skipping update", type);
                    continue; // Skip this observer but continue with others
                }
                
                // Check if the config value has actually changed using hash comparison
                var currentHash = ComputeConfigHash(currentConfig);
                var previousHash = _previousConfigHashes.GetValueOrDefault(type, string.Empty);
                
                if (currentHash == previousHash && !string.IsNullOrEmpty(previousHash))
                {
                    // No change detected, skip emission
                    continue;
                }
                
                // Store the new hash for next comparison
                _previousConfigHashes[type] = currentHash;
                
                // Emit the current value to the subject
                if (subject is ISubject<object?> objectSubject)
                {
                    objectSubject.OnNext(currentConfig);
                }
                else
                {
                    // For generic subjects, we need to call OnNext with the proper type
                    var onNextMethod = subject.GetType().GetMethod("OnNext");
                    if (onNextMethod != null)
                    {
                        // Convert to the specific generic type if needed
                        var convertedValue = currentConfig;
                        if (currentConfig != null && !type.IsAssignableFrom(currentConfig.GetType()))
                        {
                            // Try to handle interface bindings
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify configuration observer for type {Type}", kvp.Key);
                // Continue with other observers even if this one fails
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
    }
}