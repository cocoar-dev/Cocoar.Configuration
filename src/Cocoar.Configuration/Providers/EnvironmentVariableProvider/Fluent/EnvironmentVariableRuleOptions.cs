using Cocoar.Configuration.Providers.EnvironmentVariableProvider;

namespace Cocoar.Configuration.Fluent.ProviderOptions;

// Combined options for Environment provider (instance + query) for fluent API
public sealed class EnvironmentVariableRuleOptions
{
    private readonly string? _keyPrefix;
    private readonly string? _wrapperPath;

    /// <summary>
    /// Create options for the environment variable provider.
    /// keyPrefix filters environment variables by the given prefix (e.g. "MYAPP_"),
    /// wrapperPath optionally wraps the resulting object under a property path in the target config.
    /// </summary>
    public EnvironmentVariableRuleOptions(string? keyPrefix = null, string? wrapperPath = null)
    {
        _keyPrefix = keyPrefix;
        _wrapperPath = wrapperPath;
    }

    /// <summary>
    /// Preferred factory with clearer names.
    /// </summary>
    public static EnvironmentVariableRuleOptions FromPrefix(string? keyPrefix, string? wrapperPath = null)
        => new(keyPrefix: keyPrefix, wrapperPath: wrapperPath);


    public EnvironmentVariableProviderOptions ToProviderOptions() => new(_keyPrefix);
    // Provider currently uses query.KeyPrefix as the prefix filter; map KeyPrefix here for correct behavior
    public EnvironmentVariableProviderQueryOptions ToQueryOptions() => new(_keyPrefix, _wrapperPath);
}
