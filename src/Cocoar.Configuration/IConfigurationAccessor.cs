namespace Cocoar.Configuration;

public interface IConfigurationAccessor
{
    T? GetConfig<T>();
    object? GetConfig(Type type);
}
