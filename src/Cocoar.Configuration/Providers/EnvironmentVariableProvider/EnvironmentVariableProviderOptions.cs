namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProviderOptions : ISourceProviderInstanceOptions
{
    public string? Prefix { get; }
    public EnvironmentVariableProviderOptions(string? prefix = null)
    {
        Prefix = prefix;
    }
}
