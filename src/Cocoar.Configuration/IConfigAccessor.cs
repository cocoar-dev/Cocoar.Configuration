namespace Cocoar.Configuration;

public interface IConfigAccessor
{
    T? GetConfig<T>();
    object? GetConfig(Type type);
}
