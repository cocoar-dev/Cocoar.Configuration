using System.Collections.Concurrent;
using System.Text.Json;

namespace Cocoar.Configuration.Extensions;

public class ConfigManager
{

    private readonly List<ConfigRule> _rules;
    private volatile Dictionary<Type, JsonElement> _configs = new();
    private bool _initialized = false;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<(Type type, string key), IConfigSourceProvider> _providerCache = new();
    public ConfigManager(IServiceProvider serviceProvider, IEnumerable<ConfigRule> rules)
    {
        _serviceProvider = serviceProvider;
        _rules = rules.ToList();
    }

    public ConfigManager Initialize()
    {
        if (!_initialized)
        {
            RecalculateAllConfigsAsync().GetAwaiter().GetResult();
            _initialized = true;
        }
        
        return this;
    }

    private async Task RecalculateAllConfigsAsync(CancellationToken cancellationToken = default)
    {
        var newDict = new Dictionary<Type, JsonElement>();

        foreach (var rule in _rules)
        {
            var provider = GetOrCreateProvider(rule.ProviderType, rule.ProviderKey);

            // await the provider for the correct section/part
            var value = await provider.GetValueAsync(rule.SectionName, cancellationToken);
            if (!value.HasValue)
                continue;

            if (!newDict.TryGetValue(rule.ConfigContract, out var existing))
            {
                newDict[rule.ConfigContract] = value.Value;
            }
            else
            {
                newDict[rule.ConfigContract] = MergeJsonElements(existing, value.Value);
            }
        }

        _configs = newDict;
    }
    
    public JsonElement? GetConfig(Type contract)
    {
        return _configs.TryGetValue(contract, out var value) ? value : null;
    }
    
    private JsonElement MergeJsonElements(JsonElement oldValue, JsonElement newValue)
    {
        // TODO: Deep merge logic here.
        // For now, just overwrite (for prototype)
        return newValue;
    }
    
    private IConfigSourceProvider GetOrCreateProvider(Type providerType, string providerKey)
    {
        var cacheKey = (providerType, providerKey);

        if (_providerCache.TryGetValue(cacheKey, out var existing))
            return existing;

        // Try DI first
        IConfigSourceProvider? provider = _serviceProvider.GetService(providerType) as IConfigSourceProvider;

        // If not in DI, try to create manually (with key)
        if (provider == null)
        {
            provider = Activator.CreateInstance(providerType, providerKey) as IConfigSourceProvider;
        }

        if (provider == null)
            throw new InvalidOperationException($"Could not create provider {providerType.Name} with key '{providerKey}'.");

        _providerCache[cacheKey] = provider;
        return provider;
    }
}

// For now, you may need to adjust ConfigRule to store the provider key, contract type, section name:
public record ConfigRule(Type ProviderType, string ProviderKey, Type ConfigContract, string? SectionName = null, ConfigLifetime? Lifetime = null);
public record ConfigRule<T>(string ProviderKey, Type ConfigContract, string? SectionName = null, ConfigLifetime? Lifetime = null): ConfigRule(typeof(T), ProviderKey, ConfigContract, SectionName, Lifetime);

public enum ConfigLifetime
{
    Singleton,
    Scoped,
    Transient
}