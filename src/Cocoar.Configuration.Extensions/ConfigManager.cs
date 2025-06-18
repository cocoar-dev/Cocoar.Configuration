using System.Collections.Concurrent;
using System.Text.Json;
using Cocoar.Configuration.Extensions.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Extensions;

public class ConfigManager
{
    private readonly List<ConfigRule> _rules;
    private volatile Dictionary<ConfigTypeDefinition, JsonElement> _configs = new();
    private volatile bool _initialized;
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
            if (Interlocked.CompareExchange(ref _initialized, true, false) == false)
                RecalculateAllConfigsAsync().GetAwaiter().GetResult();
        }
        return this;
    }

    private async Task RecalculateAllConfigsAsync(CancellationToken cancellationToken = default)
    {
        var tempFlatMaps = new Dictionary<ConfigTypeDefinition, Dictionary<string, JsonElement>>();
        foreach (var rule in _rules)
        {
            if (rule.UseWhen != null && !rule.UseWhen.Invoke())
            {
                continue;
            }

            var provider = GetOrCreateProvider(rule);
            var value = await provider.GetValueAsync(rule.QueryOptions, cancellationToken);
            if (!value.HasValue)
                continue;
            if (!tempFlatMaps.TryGetValue(rule.ConfigContract, out var flatMap))
            {
                flatMap = new Dictionary<string, JsonElement>();
                tempFlatMaps[rule.ConfigContract] = flatMap;
            }
            var flatOutcome = Flatten(value.Value);
            foreach (var kvp in flatOutcome)
                flatMap[kvp.Key] = kvp.Value;
        }
        var nextConfig = new Dictionary<ConfigTypeDefinition, JsonElement>();
        foreach (var (type, flatMap) in tempFlatMaps)
            nextConfig[type] = Unflatten(flatMap);
        _configs = nextConfig;
    }

    private static Dictionary<string, JsonElement> Flatten(JsonElement element)
    {
        var dict = new Dictionary<string, JsonElement>();
        FlattenRec(element, null, dict);
        return dict;
    }

    private static void FlattenRec(JsonElement e, string? prefix, Dictionary<string, JsonElement> dict)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in e.EnumerateObject())
            {
                var key = prefix == null ? prop.Name : $"{prefix}.{prop.Name}";
                FlattenRec(prop.Value, key, dict);
            }
        }
        else if (prefix != null)
        {
            dict[prefix] = e;
        }
    }

    private static JsonElement Unflatten(Dictionary<string, JsonElement> flat)
    {
        var root = new Dictionary<string, object>();
        foreach (var (path, value) in flat)
        {
            var segs = path.Split('.');
            var cursor = root;
            for (int i = 0; i < segs.Length - 1; i++)
            {
                if (!cursor.TryGetValue(segs[i], out var child) || !(child is Dictionary<string, object> childDict))
                {
                    childDict = new Dictionary<string, object>();
                    cursor[segs[i]] = childDict;
                }
                cursor = childDict;
            }
            cursor[segs[^1]] = value;
        }
        var json = JsonSerializer.Serialize(root);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public T? GetConfig<T>()
    {
        var configType = _configs.Keys.FirstOrDefault(k => k.ConfigType == typeof(T));
        
        return configType is null ? default : _configs.TryGetValue(configType, out var value) ? (T?)value.Deserialize(configType.ImplementationType ?? configType.ConfigType): default;
    }

    public object? GetConfig(Type type)
    {
        var configType = new ConfigTypeDefinition(type);
        return _configs.TryGetValue(new ConfigTypeDefinition(type), out var value) ? value.Deserialize(configType.ImplementationType ?? configType.ConfigType) : null;
    }

    public JsonElement? GetConfigAsJson(Type type)
    {
        return _configs.TryGetValue(new ConfigTypeDefinition(type), out var value) ? value.Clone() : null;
    }

    private ConfigSourceProvider GetOrCreateProvider(ConfigRule rule)
    {
        var cacheKey = (rule.ProviderType, rule.ProviderOptions.CalculateKey());
        if (_providerCache.TryGetValue(cacheKey, out var existing))
            return existing;
        ConfigSourceProvider? provider = _serviceProvider.GetService(rule.ProviderType) as ConfigSourceProvider;
        if (provider == null)
        {
            provider = (ConfigSourceProvider?)ActivatorUtilities.CreateInstance(_serviceProvider, rule.ProviderType, rule.ProviderOptions);
        }
        if (provider == null)
            throw new InvalidOperationException($"Could not create provider {rule.ProviderType.Name} with key ''.");
        _providerCache[cacheKey] = provider;
        return provider;
    }
}
