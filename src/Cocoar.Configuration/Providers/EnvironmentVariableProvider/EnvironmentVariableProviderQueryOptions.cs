using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProviderQueryOptions(string? keyPrefix = null, string? wrapperPath = null)
    : ISourceProviderQueryOptions
{
    // KeyPrefix is used as the prefix for environment variable filtering
    public string? KeyPrefix { get; } = keyPrefix;
    public string? WrapperPath { get; } = wrapperPath;

}
