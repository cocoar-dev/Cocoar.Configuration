using System.Text.Json;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Core;

internal class ConfigurationAccessor(ConfigurationRepository repository, BindingRegistry bindingRegistry)
    : IConfigurationAccessor
{
    public T? GetConfig<T>()
    {
        return (T?)ResolveConfig(typeof(T));
    }

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
            throw new InvalidOperationException($"Configuration for type {typeof(T).Name} not found.");
        }
        return result;
    }

    public object? GetConfig(Type type)
    {
        return ResolveConfig(type);
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

    public JsonElement? GetConfigAsJson(Type type) => repository.GetConfigurationAsJson(type);

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

        if (!repository.TryGetConfiguration(type, out var jsonElement))
        {
            return false;
        }

        var registration = repository.FindRegistration(type);
        if (registration == null)
        {
            return false;
        }

        value = ConfigurationDeserializer.Deserialize(jsonElement, registration);
        return true;
    }
}
