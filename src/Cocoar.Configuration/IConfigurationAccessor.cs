using System.Text.Json;

namespace Cocoar.Configuration;

public interface IConfigurationAccessor
{
    T? GetConfig<T>();
    bool TryGetConfig<T>(out T? value);
    T GetRequiredConfig<T>();
    object? GetConfig(Type type);
    bool TryGetConfig(Type type, out object? value);
    object GetRequiredConfig(Type type);
    JsonElement? GetConfigAsJson(Type type);
}
