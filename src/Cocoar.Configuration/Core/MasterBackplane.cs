using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Cocoar.Configuration.Infrastructure;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Single source of truth for all configuration instances.
/// Provides atomic updates across all types and type-specific projections for reactive consumers.
/// </summary>
internal sealed class MasterBackplane : IDisposable
{
    private readonly BehaviorSubject<ConfigSnapshot> _snapshotSubject;
    private readonly ExposureRegistry _bindingRegistry;
    private readonly ConcurrentDictionary<Type, object> _typeProjectionCache = new();
    private readonly Lock _publishLock = new();
    private bool _disposed;

    public MasterBackplane(ExposureRegistry bindingRegistry)
    {
        _bindingRegistry = bindingRegistry;
        _snapshotSubject = new BehaviorSubject<ConfigSnapshot>(ConfigSnapshot.Empty);
    }

    /// <summary>
    /// Gets the current configuration snapshot.
    /// </summary>
    public ConfigSnapshot CurrentSnapshot => _snapshotSubject.Value;

    /// <summary>
    /// Observable stream of configuration snapshots.
    /// Emits whenever a new snapshot is published.
    /// </summary>
    public IObservable<ConfigSnapshot> SnapshotStream => _snapshotSubject.AsObservable();

    /// <summary>
    /// Publishes a new configuration snapshot atomically.
    /// All type projections will be updated in a single operation.
    /// </summary>
    /// <param name="snapshot">The new snapshot to publish.</param>
    public void Publish(ConfigSnapshot snapshot)
    {
        lock (_publishLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _snapshotSubject.OnNext(snapshot);
        }
    }

    /// <summary>
    /// Gets an observable projection for a specific configuration type.
    /// Uses ReferenceEquals for efficient change detection.
    /// </summary>
    /// <typeparam name="T">The configuration type to project.</typeparam>
    /// <returns>An observable that emits when the configuration instance reference changes.</returns>
    public IObservable<T> GetTypeProjection<T>() where T : class
    {
        var type = typeof(T);
        return (IObservable<T>)_typeProjectionCache.GetOrAdd(type, _ => CreateTypeProjection<T>());
    }

    /// <summary>
    /// Gets a configuration instance from the current snapshot.
    /// Supports interface-to-concrete type mapping.
    /// </summary>
    /// <typeparam name="T">The configuration type to retrieve.</typeparam>
    /// <returns>The configuration instance, or null if not found.</returns>
    public T? GetConfig<T>() where T : class
    {
        var snapshot = CurrentSnapshot;

        // Try direct type lookup first
        var result = snapshot.GetConfig<T>();
        if (result != null)
        {
            return result;
        }

        // Try interface-to-concrete mapping
        if (_bindingRegistry.TryGetConcreteType(typeof(T), out var concreteType))
        {
            var concrete = snapshot.GetConfig(concreteType);
            if (concrete is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a configuration instance from the current snapshot.
    /// Supports interface-to-concrete type mapping.
    /// </summary>
    /// <param name="type">The configuration type to retrieve.</param>
    /// <returns>The configuration instance, or null if not found.</returns>
    public object? GetConfig(Type type)
    {
        var snapshot = CurrentSnapshot;

        // Try direct type lookup first
        var result = snapshot.GetConfig(type);
        if (result != null)
        {
            return result;
        }

        // Try interface-to-concrete mapping
        if (_bindingRegistry.TryGetConcreteType(type, out var concreteType))
        {
            return snapshot.GetConfig(concreteType);
        }

        return null;
    }

    private IObservable<T> CreateTypeProjection<T>() where T : class
    {
        // Project the snapshot stream to the specific type
        // Use DistinctUntilChanged with ReferenceEquals for efficient change detection
        return _snapshotSubject
            .Select(snapshot =>
            {
                // First try direct lookup
                var config = snapshot.GetConfig<T>();
                if (config != null) return config;

                // Then try interface mapping
                if (_bindingRegistry.TryGetConcreteType(typeof(T), out var concreteType))
                {
                    var concrete = snapshot.GetConfig(concreteType);
                    if (concrete is T typed) return typed;
                }

                return null!;
            })
            .Where(config => config != null)
            .DistinctUntilChanged(ReferenceEqualityComparer<T>.Instance);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _snapshotSubject.OnCompleted();
        _snapshotSubject.Dispose();
        _typeProjectionCache.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Comparer that uses ReferenceEquals for efficient change detection.
    /// </summary>
    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
