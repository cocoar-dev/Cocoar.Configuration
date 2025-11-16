using System.Text.Json;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Capabilities;
using Cocoar.Configuration.Utilities;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

internal class ConfigurationAccessor(ConfigurationState state, ExposureRegistry bindingRegistry, ConfigManagerCapabilityScope capabilityScope)
    : IConfigurationAccessor
{
    private readonly ConfigManagerCapabilityScope _capabilityScope = capabilityScope;
    public T? GetConfig<T>() => (T?)ResolveConfig(typeof(T));

    public bool TryGetConfig<T>(out T? value)
    {
        value = GetConfig<T>();
        return value is not null;
    }

    public T GetRequiredConfig<T>()
    {
        var result = GetConfig<T>();
        if (result is null)
        {
            throw new InvalidOperationException(
                $"Configuration for {typeof(T).Name} hasn't been loaded yet. " +
                $"If you're calling this from a rule factory, ensure a rule for {typeof(T).Name} appears earlier in your rule list.");
        }
        return result;
    }

    public object? GetConfig(Type type) => ResolveConfig(type);

    public bool TryGetConfig(Type type, out object? value)
    {
        value = GetConfig(type);
        return value is not null;
    }

    public object GetRequiredConfig(Type type)
    {
        var value = GetConfig(type);
        if (value == null)
        {
            throw new InvalidOperationException(
                $"Configuration for {type.Name} hasn't been loaded yet. " +
                $"If you're calling this from a rule factory, ensure a rule for {type.Name} appears earlier in your rule list.");
        }
        return value;
    }

    public JsonElement? GetConfigAsJson(Type type) => state.GetConfigurationAsJson(type);

    private object? ResolveConfig(Type requestedType)
    {
        if (TryResolveConfiguration(requestedType, out var value))
        {
            return value;
        }

        if (bindingRegistry.TryGetConcreteType(requestedType, out var concreteType) &&
            TryResolveConfiguration(concreteType, out var concreteValue))
        {
            return concreteValue;
        }

        return null;
    }

    private bool TryResolveConfiguration(Type type, out object? value)
    {
        value = null;

        if (!state.TryGetConfiguration(type, out var mutableJsonObject) || mutableJsonObject == null)
        {
            return false;
        }

        var registration = state.FindRegistration(type);
        if (registration == null)
        {
            return false;
        }
        // Lock on the mutableJsonObject to prevent concurrent modification during serialization
        byte[] bytes;
        lock (mutableJsonObject)
        {
            bytes = MutableJsonDocument.ToUtf8Bytes(mutableJsonObject);
        }
        
        using var doc = JsonDocument.Parse(bytes);
        var jsonElement = doc.RootElement.Clone();
        value = ConfigurationDeserializer.Deserialize(jsonElement, registration, bindingRegistry.DeserializationMap, _capabilityScope);
        return true;
    }
    
}
