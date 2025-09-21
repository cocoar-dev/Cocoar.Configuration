using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class EnvironmentVariableProviderQueryOptions(string? environmentPrefix = null)
    : IProviderQuery
{
    // EnvironmentPrefix is used as the prefix for environment variable filtering
    public string? EnvironmentPrefix { get; } = environmentPrefix;
}
