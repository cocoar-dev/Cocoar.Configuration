using System.Collections.Concurrent;
using System.Text.Json;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration;

public class ConfigManager : IConfigAccessor
{
    private readonly List<ConfigRule> _rules;
    private volatile Dictionary<ConfigTypeDefinition, JsonElement> _configs = new();
    private volatile bool _initialized;
    private readonly ConcurrentDictionary<(Type type, string key), ConfigSourceProvider> _providerCache = new();
    private readonly List<IDisposable> _changeSubscriptions = new();
    private readonly object _recalcLock = new();
    private readonly IConfigLogger _logger;

    public ConfigManager(IEnumerable<ConfigRule> rules, IConfigLogger? logger = null)
    {
        _rules = rules.ToList();
        _logger = logger ?? NullConfigLogger.Instance;
    }

    public ConfigManager Initialize()
    {
        if (_initialized) return this;
        if (Interlocked.CompareExchange(ref _initialized, true, false) == false)
        {
            // initial compute and subscriptions
            RecalculateAllConfigsAsync().GetAwaiter().GetResult();
            RebuildProvidersAndSubscriptions();
        }
        return this;
    }

    private void DisposeSubscriptionsAndProviders()
    {
        foreach (var d in _changeSubscriptions.ToArray())
        {
            try { d.Dispose(); } catch { /* ignore */ }
        }
        _changeSubscriptions.Clear();

    // If providers become disposable in future, they can be disposed here.
        _providerCache.Clear();
    }

    private void RecalculateAllConfigsSafe()
    {
        // Prevent concurrent recomputes and ensure atomic swap
        lock (_recalcLock)
        {
            _logger.Debug("Recompute started");
            RecalculateAllConfigsAsync().GetAwaiter().GetResult();
            RebuildProvidersAndSubscriptions();
            _logger.Debug("Recompute finished");
        }
    }

    private async Task RecalculateAllConfigsAsync(CancellationToken cancellationToken = default)
    {
        // flat maps by config contract, merged by rule order (last wins)
        var tempFlatMaps = new Dictionary<ConfigTypeDefinition, Dictionary<string, JsonElement>>();

        foreach (var rule in _rules)
        {
            if (rule.Options?.UseWhen != null && !rule.Options.UseWhen.Invoke())
            {
                _logger.Information("Rule skipped due to useWhen=false: {0}->{1}", rule.ProviderType.Name, rule.ConfigContract.ConfigType.Name);
                continue;
            }

            var provider = GetOrCreateProvider(rule);
            var q = rule.ResolveQueryOptions(this);

            try
            {
                var value = await provider.GetValueAsync(q, cancellationToken);

                if (!tempFlatMaps.TryGetValue(rule.ConfigContract, out var flatMap))
                {
                    flatMap = new Dictionary<string, JsonElement>();
                    tempFlatMaps[rule.ConfigContract] = flatMap;
                }
                var flatOutcome = Flatten(value);
                foreach (var kvp in flatOutcome)
                    flatMap[kvp.Key] = kvp.Value; // last rule wins per key
            }
            catch (Exception ex)
            {
                if (rule.Options?.Required == true)
                {
                    _logger.Error(ex, "Required rule failed: {0}->{1}", rule.ProviderType.Name, rule.ConfigContract.ConfigType.Name);
                    throw new InvalidOperationException($"Required rule failed for {rule.ProviderType.Name} → {rule.ConfigContract.ConfigType.Name}", ex);
                }
                _logger.Warning(ex, "Optional rule failed and will be skipped: {0}->{1}", rule.ProviderType.Name, rule.ConfigContract.ConfigType.Name);
                // optional rule: skip on errors
            }
        }

        var nextConfig = new Dictionary<ConfigTypeDefinition, JsonElement>();
        foreach (var (type, flatMap) in tempFlatMaps)
            nextConfig[type] = Unflatten(flatMap);

        _configs = nextConfig;
    }

    private void RebuildProvidersAndSubscriptions()
    {
        // Recreate providers and subscriptions with possibly changed options
        // under lock to avoid races with change notifications
        DisposeSubscriptionsAndProviders();

        foreach (var rule in _rules)
        {
            if (rule.Options?.UseWhen != null && !rule.Options.UseWhen.Invoke())
                continue;

            var provider = GetOrCreateProvider(rule);
            var q = rule.ResolveQueryOptions(this);
            var sub = provider
                .Changes(q)
                .Subscribe(
                    _ =>
                    {
                        try { RecalculateAllConfigsSafe(); }
                        catch (Exception ex) { _logger.Error(ex, "Recompute failed from change trigger"); }
                    },
                    _ =>
                    {
                        try { RecalculateAllConfigsSafe(); }
                        catch (Exception ex) { _logger.Error(ex, "Recompute failed from change error trigger"); }
                    }
                );
            _changeSubscriptions.Add(sub);
        }
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
        var key = _configs.Keys.FirstOrDefault(k => k.ConfigType == typeof(T)) 
                  ?? _configs.Keys.FirstOrDefault(k => k.ImplementationType == typeof(T));

        if (key is null || !_configs.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Configuration for type {typeof(T).Name} not found.");
        }

        var target = key.ImplementationType ?? key.ConfigType;
        return (T?)Deserialize(value, target);
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
        var key = _configs.Keys.FirstOrDefault(k => k.ConfigType == type) 
                  ?? _configs.Keys.FirstOrDefault(k => k.ImplementationType == type);

        if (key is null || !_configs.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Configuration for type {type.Name} not found.");
        }
        var target = key.ImplementationType ?? key.ConfigType;
        var result = Deserialize(value, target);
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

        var configType = _configs.Keys.FirstOrDefault(k => k.ConfigType == typeof(T)) ?? _configs.Keys.FirstOrDefault(k => k.ImplementationType == typeof(T));

        if (configType is null || !_configs.TryGetValue(configType, out var value))
        {
            throw new InvalidOperationException($"Configuration for type {typeof(T).Name} not found.");
        }

        var options = new JsonSerializerOptions();
        // Register converters for common primitives
        options.Converters.Add(new StringToPrimitiveConverter<bool>());
        options.Converters.Add(new StringToPrimitiveConverter<int>());
        options.Converters.Add(new StringToPrimitiveConverter<double>());
        options.Converters.Add(new StringToPrimitiveConverter<float>());
        options.Converters.Add(new StringToPrimitiveConverter<long>());
        options.Converters.Add(new StringToPrimitiveConverter<DateTime>());

        return (T?)element.Deserialize(configType.ConfigType, options);
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
        var providerOptions = rule.ResolveProviderOptions(this);
        var cacheKey = (rule.ProviderType, providerOptions.CalculateKey());
        if (_providerCache.TryGetValue(cacheKey, out var existing))
            return existing;

        var provider = (ConfigSourceProvider?)Activator.CreateInstance(rule.ProviderType, providerOptions);
        
        if (provider == null)
            throw new InvalidOperationException($"Could not create provider {rule.ProviderType.Name} with key ''.");
        _providerCache[cacheKey] = provider;
        return provider;
    }
}
