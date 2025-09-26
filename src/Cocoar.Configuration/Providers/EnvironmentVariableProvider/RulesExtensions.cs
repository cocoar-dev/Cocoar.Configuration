using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class EnvironmentVariableRulesExtensions
{
    public static
        ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
            EnvironmentVariableProviderQueryOptions> Environment(this Rule.Dsl _,
            Func<IConfigurationAccessor, EnvironmentVariableRuleOptions> optionsFactory)
        => Rule
            .FromProvider<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
                EnvironmentVariableProviderQueryOptions>(
                cm => optionsFactory(cm).ToProviderOptions(),
                cm => optionsFactory(cm).ToQueryOptions()
            );

    public static
        ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions,
            EnvironmentVariableProviderQueryOptions> Environment(this Rule.Dsl _, string? environmentPrefix = null)
        => _.Environment(_ => EnvironmentVariableRuleOptions.FromPrefix(environmentPrefix));
}
