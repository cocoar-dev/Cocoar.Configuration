using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class EnvironmentVariableRulesExtensions
{
    /// <summary>
    /// Creates an environment variable configuration rule with custom options.
    /// </summary>
    public static
        ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
            EnvironmentVariableProviderQueryOptions> Environment(this RulesBuilder builder,
            Func<IConfigurationAccessor, EnvironmentVariableRuleOptions> optionsFactory)
        => builder
            .FromProvider<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
                EnvironmentVariableProviderQueryOptions>(
                cm => new(optionsFactory(cm).EnvironmentPrefix),
                cm => new(optionsFactory(cm).EnvironmentPrefix)
            );

    /// <summary>
    /// Creates an environment variable configuration rule with an optional prefix.
    /// </summary>
    public static
        ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
            EnvironmentVariableProviderQueryOptions> Environment(this RulesBuilder builder, string? environmentPrefix = null)
        => builder.Environment(_ => new (environmentPrefix));
}
