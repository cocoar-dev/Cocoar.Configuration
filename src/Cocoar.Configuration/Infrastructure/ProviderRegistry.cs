using System.Collections.Concurrent;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Utilities;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Infrastructure;

internal static partial class ProviderRegistryLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "ProviderRegistry: created non-reusable {Provider} (null key)")]
    public static partial void ProviderCreatedNonReusable(this ILogger logger, string Provider);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "ProviderRegistry: created {Provider} with key {Key}")]
    public static partial void ProviderCreatedWithKey(this ILogger logger, string Provider, string Key);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "ProviderRegistry: acquire {Provider} {Key} -> RefCount={RefCount}")]
    public static partial void ProviderAcquire(this ILogger logger, string Provider, string Key, int RefCount);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "ProviderRegistry: release {Provider} {Key} -> RefCount={RefCount}")]
    public static partial void ProviderRelease(this ILogger logger, string Provider, string Key, int RefCount);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "ProviderRegistry: disposing {Provider} {Key}")]
    public static partial void ProviderDisposing(this ILogger logger, string Provider, string Key);
}

internal sealed class ProviderRegistry(
    ILogger? logger = null,
    bool enableDiagnostics = false,
    Func<Type, IProviderConfiguration, ConfigurationProvider>? factory = null)
{
    private readonly ConcurrentDictionary<(Type type, string key), Entry> _entries = new();
    private readonly ILogger _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    internal sealed class Entry
    {
        public required ConfigurationProvider Provider { get; init; }
        public int RefCount;
    }

    public sealed class ProviderHandle : IDisposable
    {
        private readonly ProviderRegistry? _owner;
        private readonly (Type type, string key)? _id;
        private Entry? _entry;
        private readonly bool _isReusable;

        private ProviderHandle(ProviderRegistry owner, (Type type, string key) id, Entry entry)
        {
            _owner = owner;
            _id = id;
            _entry = entry;
            _isReusable = true;
        }
        
        private ProviderHandle(Entry entry)
        {
            _owner = null;
            _id = null;
            _entry = entry;
            _isReusable = false;
        }

        internal static ProviderHandle Create(ProviderRegistry owner, (Type type, string key) id, Entry entry) =>
            new(owner, id, entry);
            
        internal static ProviderHandle CreateNonReusable(ProviderRegistry owner, Entry entry) => new(entry);

        public ConfigurationProvider Provider
            => _entry?.Provider ?? throw new ObjectDisposedException(nameof(ProviderHandle));

        public void Dispose()
        {
            var e = Interlocked.Exchange(ref _entry, null);
            if (e is null)
            {
                return;
            }

            if (_isReusable && _owner is not null && _id.HasValue)
            {
                _owner.Release(_id.Value, e);
            }
            else
            {
                if (e.Provider is IDisposable disp)
                {
                    Safety.DisposeQuietly(disp);
                }
            }
        }
    }

    public ProviderHandle Acquire(Type providerType, IProviderConfiguration options)
    {
        var key = options.GenerateProviderKey();
        
        if (key is null)
        {
            var provider = CreateProvider(providerType, options);
            if (enableDiagnostics)
            {
                _logger.ProviderCreatedNonReusable(providerType.Name);
            }

            var nonReusableEntry = new Entry
            {
                Provider = provider,
                RefCount = 1
            };
            
            return ProviderHandle.CreateNonReusable(this, nonReusableEntry);
        }
        
        var id = (providerType, key);
        var isNewEntry = false;
        var entry = _entries.GetOrAdd(id, _ =>
        {
            isNewEntry = true;
            var created = new Entry
            {
                Provider = CreateProvider(providerType, options),
                RefCount = 1  // Start at 1 to prevent race condition
            };
            if (enableDiagnostics)
            {
                _logger.ProviderCreatedWithKey(providerType.Name, key);
            }

            return created;
        });
        var newCount = isNewEntry ? 1 : Interlocked.Increment(ref entry.RefCount);
        if (enableDiagnostics)
        {
            _logger.ProviderAcquire(providerType.Name, key, newCount);
        }

        return ProviderHandle.Create(this, id, entry);
    }

    private void Release((Type type, string key) id, Entry entry)
    {
        var count = Interlocked.Decrement(ref entry.RefCount);
        if (enableDiagnostics)
        {
            _logger.ProviderRelease(id.type.Name, id.key, count);
        }

        if (count != 0)
        {
            return;
        }

        if (!_entries.TryRemove(id, out var removed) || !ReferenceEquals(removed, entry))
        {
            return;
        }

        if (removed.Provider is not IDisposable disp)
        {
            return;
        }

        if (enableDiagnostics)
        {
            _logger.ProviderDisposing(id.type.Name, id.key);
        }

        Safety.DisposeQuietly(disp);
    }

    private ConfigurationProvider CreateProvider(Type providerType, IProviderConfiguration options)
    {
        if (factory is not null)
        {
            var inst = factory(providerType, options);
            if (inst == null)
            {
                throw new InvalidOperationException("Factory produced null provider instance.");
            }

            return inst;
        }
        var instance = Activator.CreateInstance(providerType, options) as ConfigurationProvider
                       ?? throw new InvalidOperationException($"Could not create provider {providerType.Name}.");
        return instance;
    }
}
