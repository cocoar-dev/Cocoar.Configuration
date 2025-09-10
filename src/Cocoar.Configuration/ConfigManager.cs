using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.Configuration;

public class ConfigManager : IConfigAccessor, IDisposable
{
    private readonly List<ConfigRule> _rules;
    private volatile Dictionary<ConfigTypeDefinition, JsonElement> _configs = new();
    // Working snapshot used during recompute so later rules can see earlier merges
    private volatile Dictionary<ConfigTypeDefinition, JsonElement>? _workingConfigs;
    private volatile bool _initialized;
    private readonly List<IDisposable> _changeSubscriptions = new();
    private readonly Lock _recalcLock = new();
    private readonly ILogger _logger;
    private readonly List<RuleManager> _ruleManagers = new();
    private readonly ProviderRegistry _providerRegistry;

    public ConfigManager(IEnumerable<ConfigRule> rules, ILogger? logger = null)
    {
        _rules = rules.ToList();
        _logger = logger ?? NullLogger.Instance;
        _providerRegistry = new ProviderRegistry(_logger);
    }

    public ConfigManager Initialize()
    {
        if (_initialized) return this;
        if (Interlocked.CompareExchange(ref _initialized, true, false) == false)
        {
            // Create per-rule managers
            _ruleManagers.Clear();
            foreach (var r in _rules)
                _ruleManagers.Add(new RuleManager(r, _logger, _providerRegistry));
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

        // Providers are owned by RuleManagers; nothing to clear here.
    }

    private void RecalculateAllConfigsSafe()
    {
        // Prevent concurrent recomputes and ensure atomic swap
        lock (_recalcLock)
        {
            _logger.LogDebug("Recompute started");
            RecalculateAllConfigsAsync().GetAwaiter().GetResult();
            RebuildProvidersAndSubscriptions();
            _logger.LogDebug("Recompute finished");
        }
    }

    private async Task RecalculateAllConfigsAsync(CancellationToken cancellationToken = default)
    {
        // flat maps by config contract, merged by rule order (last wins)
        var tempFlatMaps = new Dictionary<ConfigTypeDefinition, Dictionary<string, JsonElement>>();
        // install working snapshot for in-progress reads
        _workingConfigs = new Dictionary<ConfigTypeDefinition, JsonElement>();

        foreach (var rm in _ruleManagers)
        {
            var (include, value) = await rm.ComputeAsync(this, cancellationToken).ConfigureAwait(false);
            if (!include) continue;

            if (!tempFlatMaps.TryGetValue(rm.TypeDefinition, out var flatMap))
            {
                flatMap = new Dictionary<string, JsonElement>();
                tempFlatMaps[rm.TypeDefinition] = flatMap;
            }
            var flatOutcome = Flatten(value);
            foreach (var kvp in flatOutcome)
                flatMap[kvp.Key] = kvp.Value; // last rule wins per key

            // Update working snapshot for this type so subsequent rules can read it
            var partial = Unflatten(flatMap);
            _workingConfigs[rm.TypeDefinition] = partial;
        }

        var nextConfig = new Dictionary<ConfigTypeDefinition, JsonElement>();
        foreach (var (type, flatMap) in tempFlatMaps)
            nextConfig[type] = Unflatten(flatMap);

        _configs = nextConfig;
        _workingConfigs = null; // clear working snapshot after atomic swap
    }

    private void RebuildProvidersAndSubscriptions()
    {
        // Recreate providers and subscriptions with possibly changed options
        // under lock to avoid races with change notifications
        DisposeSubscriptionsAndProviders();

        foreach (var rm in _ruleManagers)
        {
            var sub = rm.Changes
                .Subscribe(_ =>
                {
                    try { RecalculateAllConfigsSafe(); }
                    catch (Exception ex) { _logger.LogError(ex, "Recompute failed from change trigger"); }
                });
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
                var key = prefix == null ? prop.Name : $"{prefix}:{prop.Name}";
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
            var segments = path.Split(':');
            var cursor = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (!cursor.TryGetValue(segments[i], out var child) || child is not Dictionary<string, object> childDict)
                {
                    childDict = new Dictionary<string, object>();
                    cursor[segments[i]] = childDict;
                }
                cursor = childDict;
            }
            cursor[segments[^1]] = value;
        }
        var json = JsonSerializer.Serialize(root);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public T? GetConfig<T>()
    {
        Dictionary<ConfigTypeDefinition, JsonElement> map = _workingConfigs ?? _configs;
        var key = map.Keys.FirstOrDefault(k => k.ConfigType == typeof(T))
                  ?? map.Keys.FirstOrDefault(k => k.ImplementationType == typeof(T));
        if (key is null || !map.TryGetValue(key, out var value))
            return default;
        var target = key.ConfigType;
        return (T?)Deserialize(value, target);
    }

    public bool TryGetConfig<T>(out T? value)
    {
        value = GetConfig<T>();
        return value is not null;
    }

    public T GetRequiredConfig<T>()
    {
        Dictionary<ConfigTypeDefinition, JsonElement> map = _workingConfigs ?? _configs;
        var configType = map.Keys.FirstOrDefault(k => k.ConfigType == typeof(T))
                 ?? map.Keys.FirstOrDefault(k => k.ImplementationType == typeof(T));

        if (configType is null || !map.TryGetValue(configType, out var value))
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
        var map = _workingConfigs ?? _configs;
        var key = map.Keys.FirstOrDefault(k => k.ConfigType == type)
                  ?? map.Keys.FirstOrDefault(k => k.ImplementationType == type);
        if (key is null || !map.TryGetValue(key, out var value))
            return null;
        var target = key.ConfigType;
        var result = Deserialize(value, target);
        return result;
    }

    public bool TryGetConfig(Type type, out object? value)
    {
        value = GetConfig(type);
        return value is not null;
    }

    public object GetRequiredConfig(Type type)
    {
        var value = GetConfig(type);
        return value ?? throw new InvalidOperationException($"Configuration for type {type.Name} not found.");
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

        return element.Deserialize<T>(options);
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

    // Providers are resolved and managed by RuleManager.

    public void Dispose()
    {
        DisposeSubscriptionsAndProviders();
        foreach (var rm in _ruleManagers.ToArray())
        {
            try { rm.Dispose(); } catch { /* ignore */ }
        }
        _ruleManagers.Clear();
        _initialized = false;
        GC.SuppressFinalize(this);
    }
}
