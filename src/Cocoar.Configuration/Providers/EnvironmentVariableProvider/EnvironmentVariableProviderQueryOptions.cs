using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProviderQueryOptions(string? environmentPrefix = null, string? targetPath = null)
    : IProviderQuery
{
    // EnvironmentPrefix is used as the prefix for environment variable filtering
    public string? EnvironmentPrefix { get; } = environmentPrefix;
    public string? TargetPath { get; } = targetPath;

}
