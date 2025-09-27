using System.Collections.Concurrent;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Utilities;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Infrastructure;

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

    //internal int EntryCount => _entries.Count;
    //internal int GetRefCountFor(Type providerType, string key)
    //    => _entries.TryGetValue((providerType, key), out var e) ? e.RefCount : 0;

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
                _logger.LogDebug("ProviderRegistry: created non-reusable {Provider} (null key)", providerType.Name);
            }

            var nonReusableEntry = new Entry
            {
                Provider = provider,
                RefCount = 1
            };
            
            return ProviderHandle.CreateNonReusable(this, nonReusableEntry);
        }
        
        var id = (providerType, key);
        var entry = _entries.GetOrAdd(id, _ =>
        {
            var created = new Entry
            {
                Provider = CreateProvider(providerType, options),
                RefCount = 0
            };
            if (enableDiagnostics)
            {
                _logger.LogDebug("ProviderRegistry: created {Provider} with key {Key}", providerType.Name, key);
            }

            return created;
        });
        var newCount = Interlocked.Increment(ref entry.RefCount);
        if (enableDiagnostics)
        {
            _logger.LogDebug("ProviderRegistry: acquire {Provider} {Key} -> RefCount={RefCount}", providerType.Name, key, newCount);
        }

        return ProviderHandle.Create(this, id, entry);
    }

    private void Release((Type type, string key) id, Entry entry)
    {
        var count = Interlocked.Decrement(ref entry.RefCount);
        if (enableDiagnostics)
        {
            _logger.LogDebug("ProviderRegistry: release {Provider} {Key} -> RefCount={RefCount}", id.type.Name, id.key, count);
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
            _logger.LogDebug("ProviderRegistry: disposing {Provider} {Key}", id.type.Name, id.key);
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
