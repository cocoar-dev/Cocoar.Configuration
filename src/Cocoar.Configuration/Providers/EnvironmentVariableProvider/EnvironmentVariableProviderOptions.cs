using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProviderOptions : ISourceProviderInstanceOptions
{
    public string? KeyPrefix { get; }

    public EnvironmentVariableProviderOptions(string? keyPrefix = null)
    {
        KeyPrefix = keyPrefix;
    }

    // All environment lookups use the same underlying source; share a single instance across rules.
    public string CalculateKey() => "Environment:Global";
}
