namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

// Combined options for Environment provider (instance + query) for fluent API
public sealed class EnvironmentVariableRuleOptions
{
    private readonly string? _environmentPrefix;
    private readonly string? _targetPath;

    /// <summary>
    /// Create options for the environment variable provider.
    /// environmentPrefix filters environment variables by the given prefix (e.g. "MYAPP_"),
    /// targetPath optionally wraps the resulting object under a property path in the target config.
    /// </summary>
    public EnvironmentVariableRuleOptions(string? environmentPrefix = null, string? targetPath = null)
    {
        _environmentPrefix = environmentPrefix;
        _targetPath = targetPath;
    }

    /// <summary>
    /// Preferred factory with clearer names.
    /// </summary>
    public static EnvironmentVariableRuleOptions FromPrefix(string? environmentPrefix, string? targetPath = null)
        => new(environmentPrefix: environmentPrefix, targetPath: targetPath);


    public EnvironmentVariableProviderOptions ToProviderOptions() => new(_environmentPrefix);
    // Provider currently uses query.EnvironmentPrefix as the prefix filter; map environmentPrefix here for correct behavior
    public EnvironmentVariableProviderQueryOptions ToQueryOptions() => new(_environmentPrefix, _targetPath);
}
