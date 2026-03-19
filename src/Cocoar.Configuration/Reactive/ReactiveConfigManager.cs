using System.Collections.Concurrent;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Reactive;

internal static partial class ReactiveConfigManagerLog
{
    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "Recreating dead observable for configuration type {Type}")]
    public static partial void RecreatingDeadObservable(this ILogger logger, Type Type);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Failed to get initial config for type {Type}, using default value")]
    public static partial void GetInitialConfigFailed(this ILogger logger, Exception exception, Type Type);

    [LoggerMessage(EventId = 6006, Level = LogLevel.Debug, Message = "Created reactive config wrapper for type {Type}")]
    public static partial void CreatedReactiveConfig(this ILogger logger, Type type);
}

/// <summary>
/// Manages reactive configuration access using the MasterBackplane.
/// Provides type-safe projections of the configuration snapshot stream.
/// </summary>
internal sealed class ReactiveConfigManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly ExposureRegistry _bindingRegistry;
    private readonly ConcurrentDictionary<Type, object> _reactiveConfigs = new();
    private MasterBackplane? _backplane;
    private bool _disposed;

    public ReactiveConfigManager(ILogger logger, ExposureRegistry bindingRegistry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bindingRegistry = bindingRegistry ?? throw new ArgumentNullException(nameof(bindingRegistry));
    }

    /// <summary>
    /// Sets the MasterBackplane for this manager.
    /// Must be called before GetReactiveConfig.
    /// </summary>
    internal void SetBackplane(MasterBackplane backplane)
    {
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
    }

    /// <summary>
    /// Gets a reactive configuration wrapper for the specified type.
    /// Uses the MasterBackplane's type projection for efficient change detection.
    /// </summary>
    public IReactiveConfig<T> GetReactiveConfig<T>(Func<T> fallbackAccessor) where T : class
    {
        if (_backplane == null)
        {
            throw new InvalidOperationException("MasterBackplane not initialized. Ensure InitializeBackplane is called first.");
        }

        var type = typeof(T);
        return (IReactiveConfig<T>)_reactiveConfigs.GetOrAdd(type, _ =>
        {
            _logger.CreatedReactiveConfig(type);
            return new BackplaneReactiveConfig<T>(_backplane);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var config in _reactiveConfigs.Values)
        {
            if (config is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch { /* ignore */ }
            }
        }

        _reactiveConfigs.Clear();
    }

    /// <summary>
    /// IReactiveConfig implementation that uses the MasterBackplane for values.
    /// </summary>
    private sealed class BackplaneReactiveConfig<T> : IReactiveConfig<T>, IDisposable where T : class
    {
        private readonly MasterBackplane _backplane;
        private readonly IObservable<T> _observable;

        public BackplaneReactiveConfig(MasterBackplane backplane)
        {
            _backplane = backplane;
            _observable = backplane.GetTypeProjection<T>();
        }

        public T CurrentValue => _backplane.GetConfig<T>() ?? throw new InvalidOperationException($"No configuration available for type {typeof(T).Name}.");

        public IDisposable Subscribe(IObserver<T> observer) => _observable.Subscribe(observer);

        public void Dispose()
        {
            // No resources to dispose - backplane and observable are shared
        }
    }
}
