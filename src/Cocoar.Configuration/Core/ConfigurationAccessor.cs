using System.Text.Json;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Capabilities;
using Cocoar.Configuration.Utilities;
using Cocoar.Json.Mutable;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Core;

internal partial class ConfigurationAccessor : IConfigurationAccessor
{
    private readonly ConfigurationState _state;
    private readonly ExposureRegistry _bindingRegistry;
    private readonly ConfigManagerCapabilityScope _capabilityScope;
    private readonly ILogger _logger;

    public ConfigurationAccessor(
        ConfigurationState state,
        ExposureRegistry bindingRegistry,
        ConfigManagerCapabilityScope capabilityScope,
        ILogger logger)
    {
        _state = state;
        _bindingRegistry = bindingRegistry;
        _capabilityScope = capabilityScope;
        _logger = logger;
    }
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

    public JsonElement? GetConfigAsJson(Type type) => _state.GetConfigurationAsJson(type);

    private object? ResolveConfig(Type requestedType)
    {
        if (TryResolveConfiguration(requestedType, out var value))
        {
            return value;
        }

        if (_bindingRegistry.TryGetConcreteType(requestedType, out var concreteType) &&
            TryResolveConfiguration(concreteType, out var concreteValue))
        {
            return concreteValue;
        }

        return null;
    }

    private bool TryResolveConfiguration(Type type, out object? value)
    {
        value = null;

        if (!_state.TryGetConfiguration(type, out var mutableJsonObject) || mutableJsonObject == null)
        {
            return false;
        }

        var registration = _state.FindRegistration(type);
        if (registration == null)
        {
            return false;
        }

        byte[] bytes;
        lock (mutableJsonObject)
        {
            bytes = MutableJsonDocument.ToUtf8Bytes(mutableJsonObject);
        }

        using var doc = JsonDocument.Parse(bytes);
        var jsonElement = doc.RootElement.Clone();

        try
        {
            value = ConfigurationDeserializer.Deserialize(
                jsonElement, registration, _bindingRegistry.DeserializationMap, _capabilityScope);
            return value != null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidCastException)
        {
            var jsonPreview = jsonElement.ToString();
            if (jsonPreview.Length > 500)
            {
                jsonPreview = jsonPreview[..500] + "...";
            }
            DeserializationFailed(_logger, ex, type.Name, jsonPreview);
            return false;
        }
    }

    [LoggerMessage(EventId = 5100, Level = LogLevel.Error,
        Message = "Failed to deserialize configuration for {TypeName}. " +
                  "This may be caused by missing 'required' properties or type mismatches. JSON: {JsonContent}")]
    private static partial void DeserializationFailed(ILogger logger, Exception ex, string typeName, string jsonContent);
}
