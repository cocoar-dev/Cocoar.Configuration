using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Extensions;

public class ConfigManager
{

    private readonly List<ConfigRule> _rules;
    private volatile Dictionary<Type, JsonElement> _configs = new();
    private bool _initialized;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<(Type type, string key), ConfigSourceProvider> _providerCache = new();
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
            var provider = GetOrCreateProvider(rule);
            
                // await the provider for the correct section/part
                var value = await provider.GetValueAsync(rule.QueryOptions, cancellationToken);
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
    
    private ConfigSourceProvider GetOrCreateProvider(ConfigRule rule)
    {


        var cacheKey = (rule.ProviderType, rule.ProviderOptions.CalculateKey());

        

        if (_providerCache.TryGetValue(cacheKey, out var existing))
            return existing;

        // Try DI first
        ConfigSourceProvider? provider = _serviceProvider.GetService(rule.ProviderType) as ConfigSourceProvider;

       
        // If not in DI, try to create manually (with key)
        if (provider == null)
        {
            if (rule.ProviderOptions is not null)
            {
                provider =
                    ActivatorUtilities.CreateInstance(_serviceProvider, rule.ProviderType, rule.ProviderOptions) as
                        ConfigSourceProvider;
            }
            else
            {
                provider =
                    ActivatorUtilities.CreateInstance(_serviceProvider, rule.ProviderType) as
                        ConfigSourceProvider;
            }
            //provider = Activator.CreateInstance(providerType, providerKey) as ConfigSourceProvider;
        }

        if (provider == null)
            throw new InvalidOperationException($"Could not create provider {rule.ProviderType.Name} with key ''.");

        _providerCache[cacheKey] = provider;
        return provider;
    }
}

