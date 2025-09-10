using System.Collections.Concurrent;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

/// <summary>
/// Registry for sharing provider instances across rules, keyed by ProviderType + InstanceOptions.CalculateKey().
/// Manages reference counts and disposes providers when the last lease is released.
/// </summary>
internal sealed class ProviderRegistry
{
    private readonly ConcurrentDictionary<(Type type, string key), Entry> _entries = new();
    private readonly ILogger _logger;
    private readonly bool _diagnosticsEnabled;

    public ProviderRegistry(ILogger? logger = null, bool enableDiagnostics = false)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _diagnosticsEnabled = enableDiagnostics;
    }

    // Entry is internal to avoid inconsistent accessibility while keeping it scoped to the registry.
    internal sealed class Entry
    {
        public required ConfigSourceProvider Provider { get; init; }
        public int RefCount;
    }

    // Diagnostics (internal): used by tests to assert pooling behavior.
    internal int EntryCount => _entries.Count;
    internal int GetRefCountFor(Type providerType, string key)
        => _entries.TryGetValue((providerType, key), out var e) ? e.RefCount : 0;

    public sealed class ProviderHandle : IDisposable
    {
        private readonly ProviderRegistry _owner;
        private readonly (Type type, string key) _id;
        private Entry? _entry;

        private ProviderHandle(ProviderRegistry owner, (Type type, string key) id, Entry entry)
        {
            _owner = owner;
            _id = id;
            _entry = entry;
        }
        
        internal static ProviderHandle Create(ProviderRegistry owner, (Type type, string key) id, Entry entry)
            => new ProviderHandle(owner, id, entry);

        public ConfigSourceProvider Provider
            => _entry?.Provider ?? throw new ObjectDisposedException(nameof(ProviderHandle));

    public void Dispose()
        {
            var e = Interlocked.Exchange(ref _entry, null);
            if (e is null) return;
            _owner.Release(_id, e);
        }
    }

    public ProviderHandle Acquire(Type providerType, ISourceProviderInstanceOptions options)
    {
        var key = options.CalculateKey();
        var id = (providerType, key);
        var entry = _entries.GetOrAdd(id, _ =>
        {
            var created = new Entry
            {
                Provider = CreateProvider(providerType, options),
                RefCount = 0
            };
            if (_diagnosticsEnabled)
                _logger.LogDebug("ProviderRegistry: created {Provider} with key {Key}", providerType.Name, key);
            return created;
        });
        var newCount = Interlocked.Increment(ref entry.RefCount);
        if (_diagnosticsEnabled)
            _logger.LogDebug("ProviderRegistry: acquire {Provider} {Key} -> RefCount={RefCount}", providerType.Name, key, newCount);
        return ProviderHandle.Create(this, id, entry);
    }

    private void Release((Type type, string key) id, Entry entry)
    {
        var count = Interlocked.Decrement(ref entry.RefCount);
        if (_diagnosticsEnabled)
            _logger.LogDebug("ProviderRegistry: release {Provider} {Key} -> RefCount={RefCount}", id.type.Name, id.key, count);
        if (count == 0)
        {
            // Remove only if our entry is still current
            if (_entries.TryRemove(id, out var removed) && ReferenceEquals(removed, entry))
            {
                if (removed.Provider is IDisposable disp)
                {
                    if (_diagnosticsEnabled)
                        _logger.LogDebug("ProviderRegistry: disposing {Provider} {Key}", id.type.Name, id.key);
                    try { disp.Dispose(); } catch { /* ignore */ }
                }
            }
        }
    }

    private static ConfigSourceProvider CreateProvider(Type providerType, ISourceProviderInstanceOptions options)
    {
        var instance = Activator.CreateInstance(providerType, options) as ConfigSourceProvider
                       ?? throw new InvalidOperationException($"Could not create provider {providerType.Name}.");
        return instance;
    }
}
