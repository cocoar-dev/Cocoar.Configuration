using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProviderOptions : IProviderConfiguration
{
    public string? EnvironmentPrefix { get; }

    public EnvironmentVariableProviderOptions(string? environmentPrefix = null)
    {
        EnvironmentPrefix = environmentPrefix;
    }

    // All environment lookups use the same underlying source; share a single instance across rules.
    public string GenerateProviderKey() => "Environment:Global";
}
