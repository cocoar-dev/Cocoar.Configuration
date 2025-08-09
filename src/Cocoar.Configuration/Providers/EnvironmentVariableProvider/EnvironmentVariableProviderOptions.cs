namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProviderOptions : ISourceProviderInstanceOptions
{
    public string? Prefix { get; }
    public EnvironmentVariableProviderOptions(string? prefix = null)
    {
        Prefix = prefix;
    }

    // All environment lookups use the same underlying source; share a single instance across rules.
    public string CalculateKey() => "Environment:Global";
}
