using System.Collections.Concurrent;
using System.Text.Json;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration;

public class ConfigManager
{
    private readonly List<ConfigRule> _rules;
    private volatile Dictionary<ConfigTypeDefinition, JsonElement> _configs = new();
    private volatile bool _initialized;
    private readonly ConcurrentDictionary<(Type type, string key), ConfigSourceProvider> _providerCache = new();

    public ConfigManager(IEnumerable<ConfigRule> rules)
    {
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
            if (rule.Options?.UseWhen != null && !rule.Options.UseWhen.Invoke())
            {
                continue;
            }

            var provider = GetOrCreateProvider(rule);
            //provider.Changes(rule.QueryOptions).Subscribe(value =>
            //{
            //    if (!tempFlatMaps.TryGetValue(rule.ConfigContract, out var flatMap))
            //    {
            //        flatMap = new Dictionary<string, JsonElement>();
            //        tempFlatMaps[rule.ConfigContract] = flatMap;
            //    }

            //    var flatOutcome = Flatten(value);
            //    foreach (var kvp in flatOutcome)
            //        flatMap[kvp.Key] = kvp.Value;
            //});
            var value = await provider.GetValueAsync(rule.QueryOptions, cancellationToken);
            
            if (!tempFlatMaps.TryGetValue(rule.ConfigContract, out var flatMap))
            {
                flatMap = new Dictionary<string, JsonElement>();
                tempFlatMaps[rule.ConfigContract] = flatMap;
            }
            var flatOutcome = Flatten(value);
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
        var configType = _configs.Keys.FirstOrDefault(k => k.ConfigType == typeof(T)) ?? _configs.Keys.FirstOrDefault(k => k.ImplementationType == typeof(T));

        if (configType is null || !_configs.TryGetValue(configType, out var value))
        {
            throw new InvalidOperationException($"Configuration for type {typeof(T).Name} not found.");
        }

        return Deserialize<T>(value);
    }

    public T GetRequiredConfig<T>()
    {
        var configType = _configs.Keys.FirstOrDefault(k => k.ConfigType == typeof(T)) ?? _configs.Keys.FirstOrDefault(k => k.ImplementationType == typeof(T));

        if (configType is null || !_configs.TryGetValue(configType, out var value))
        {
            throw new InvalidOperationException($"Configuration for type {typeof(T).Name} not found.");
        }
        var result = Deserialize<T>(value);
        if (result is null)
        {
            throw new InvalidOperationException($"Configuration for type {typeof(T).Name} is null.");
        }
        return (T)result;
    }

    public object? GetConfig(Type type)
    {
        var configType = _configs.Keys.FirstOrDefault(k => k.ConfigType == type) ?? _configs.Keys.FirstOrDefault(k => k.ImplementationType == type);
        
        if (configType is null || !_configs.TryGetValue(configType, out var value))
        {
            throw new InvalidOperationException($"Configuration for type {type.Name} not found.");
        }
        var result = Deserialize(value, type);
        if (result is null)
        {
            throw new InvalidOperationException($"Configuration for type {type.Name} is null.");
        }
        return result;

    }


    public JsonElement? GetConfigAsJson(Type type)
    {
        return _configs.TryGetValue(new ConfigTypeDefinition(type), out var value) ? value.Clone() : null;
    }

    private T? Deserialize<T>(JsonElement element)
    {
        var options = new JsonSerializerOptions();
        // Register converters for common primitives
        options.Converters.Add(new StringToPrimitiveConverter<bool>());
        options.Converters.Add(new StringToPrimitiveConverter<int>());
        options.Converters.Add(new StringToPrimitiveConverter<double>());
        options.Converters.Add(new StringToPrimitiveConverter<float>());
        options.Converters.Add(new StringToPrimitiveConverter<long>());
        options.Converters.Add(new StringToPrimitiveConverter<DateTime>());

        return (T?)element.Deserialize(typeof(T), options);
    }

    private object? Deserialize(JsonElement element, Type type)
    {
        var options = new JsonSerializerOptions();
        // Register converters for common primitives
        options.Converters.Add(new StringToPrimitiveConverter<bool>());
        options.Converters.Add(new StringToPrimitiveConverter<int>());
        options.Converters.Add(new StringToPrimitiveConverter<double>());
        options.Converters.Add(new StringToPrimitiveConverter<float>());
        options.Converters.Add(new StringToPrimitiveConverter<long>());
        options.Converters.Add(new StringToPrimitiveConverter<DateTime>());

        return element.Deserialize(type, options);
    }

    private ConfigSourceProvider GetOrCreateProvider(ConfigRule rule)
    {
        var cacheKey = (rule.ProviderType, rule.ProviderOptions.CalculateKey());
        if (_providerCache.TryGetValue(cacheKey, out var existing))
            return existing;

           var provider = (ConfigSourceProvider?)Activator.CreateInstance(rule.ProviderType, rule.ProviderOptions);
        
        if (provider == null)
            throw new InvalidOperationException($"Could not create provider {rule.ProviderType.Name} with key ''.");
        _providerCache[cacheKey] = provider;
        return provider;
    }
}
