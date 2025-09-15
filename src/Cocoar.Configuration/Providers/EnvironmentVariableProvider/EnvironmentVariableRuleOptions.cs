namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

// Combined options for Environment provider (instance + query) for fluent API
public sealed class EnvironmentVariableRuleOptions
{
    private readonly string? _environmentPrefix;

    /// <summary>
    /// Create options for the environment variable provider.
    /// environmentPrefix filters environment variables by the given prefix (e.g. "MYAPP_").
    /// </summary>
    public EnvironmentVariableRuleOptions(string? environmentPrefix = null)
    {
        _environmentPrefix = environmentPrefix;
    }

    /// <summary>
    /// Preferred factory with clearer names.
    /// </summary>
    public static EnvironmentVariableRuleOptions FromPrefix(string? environmentPrefix)
        => new(environmentPrefix: environmentPrefix);


    public EnvironmentVariableProviderOptions ToProviderOptions() => new(_environmentPrefix);
    // Provider currently uses query.EnvironmentPrefix as the prefix filter; map environmentPrefix here for correct behavior
    public EnvironmentVariableProviderQueryOptions ToQueryOptions() => new(_environmentPrefix);
}
