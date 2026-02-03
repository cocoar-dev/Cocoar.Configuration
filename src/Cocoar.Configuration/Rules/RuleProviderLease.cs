using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Rules;

/// <summary>
/// Manages provider lifecycle for a configuration rule.
/// Handles acquisition, key-based caching, and disposal of provider handles.
/// </summary>
internal sealed class RuleProviderLease : IDisposable
{
    private readonly Type _providerType;
    private readonly ProviderRegistry _registry;

    private ProviderRegistry.ProviderHandle? _providerHandle;
    private ConfigurationProvider? _provider;
    private string? _providerKey;

    public RuleProviderLease(Type providerType, ProviderRegistry registry)
    {
        _providerType = providerType;
        _registry = registry;
    }

    /// <summary>
    /// Gets the current provider instance, or null if not acquired.
    /// </summary>
    public ConfigurationProvider? Provider => _provider;

    /// <summary>
    /// Gets the current provider key.
    /// </summary>
    public string? ProviderKey => _providerKey;

    /// <summary>
    /// Indicates whether a provider is currently held.
    /// </summary>
    public bool HasProvider => _provider != null;

    /// <summary>
    /// Ensures a provider is available for the given options.
    /// Returns true if the provider was rebuilt (options changed).
    /// </summary>
    /// <param name="providerOptions">The provider configuration options.</param>
    /// <param name="onBeforeRebuild">Callback invoked BEFORE provider rebuild (for unsubscribing/cache invalidation).</param>
    /// <returns>True if the provider was newly acquired or rebuilt.</returns>
    public bool EnsureProvider(IProviderConfiguration providerOptions, Action? onBeforeRebuild = null)
    {
        var newProviderKey = providerOptions.GenerateProviderKey();
        if (_provider != null && _providerKey == newProviderKey)
        {
            return false;
        }

        onBeforeRebuild?.Invoke();
        RebuildProvider(providerOptions, newProviderKey);
        return true;
    }

    private void RebuildProvider(IProviderConfiguration providerOptions, string? providerKey)
    {
        if (_providerHandle is not null)
        {
            Safety.DisposeQuietly(_providerHandle);
            _providerHandle = null;
        }

        _providerHandle = _registry.Acquire(_providerType, providerOptions);
        _provider = _providerHandle.Provider;
        _providerKey = providerKey;
    }

    public void Dispose()
    {
        if (_providerHandle is not null)
        {
            Safety.DisposeQuietly(_providerHandle);
            _providerHandle = null;
        }

        _provider = null;
    }
}
