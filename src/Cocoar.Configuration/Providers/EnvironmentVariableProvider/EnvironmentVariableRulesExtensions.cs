using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class EnvironmentVariableRulesExtensions
{
    /// <summary>
    /// Creates an environment variable configuration rule with an optional prefix.
    /// </summary>
    public static
        ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
            EnvironmentVariableProviderQueryOptions> FromEnvironment<T>(this TypedProviderBuilder<T> builder, string? environmentPrefix = null)
        where T : class
        => new(
            cm => new(),
            cm => new(environmentPrefix),
            typeof(T)
        );

    /// <summary>
    /// Creates an environment variable configuration rule with custom options.
    /// </summary>
    public static
        ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
            EnvironmentVariableProviderQueryOptions> FromEnvironment<T>(this TypedProviderBuilder<T> builder,
            Func<IConfigurationAccessor, EnvironmentVariableRuleOptions> optionsFactory)
        where T : class
        => new(
            cm => new(),
            cm => new(optionsFactory(cm).EnvironmentPrefix),
            typeof(T)
        );
}
