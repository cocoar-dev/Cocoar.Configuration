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
            EnvironmentVariableProviderQueryOptions> FromEnvironment<T>(this TypedRuleBuilder<T> builder, string? environmentPrefix = null)
        => new(
            cm => new(environmentPrefix),
            cm => new(environmentPrefix),
            typeof(T)
        );

    /// <summary>
    /// Creates an environment variable configuration rule with custom options.
    /// </summary>
    public static
        ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
            EnvironmentVariableProviderQueryOptions> FromEnvironment<T>(this TypedRuleBuilder<T> builder,
            Func<IConfigurationAccessor, EnvironmentVariableRuleOptions> optionsFactory)
        => new(
            cm => new(optionsFactory(cm).EnvironmentPrefix),
            cm => new(optionsFactory(cm).EnvironmentPrefix),
            typeof(T)
        );
}
