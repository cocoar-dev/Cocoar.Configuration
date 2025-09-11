using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public static class RulesExtensions
{
    public static ProviderRuleBuilder<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions> Environment(this Rule.Dsl _, Func<ConfigManager, EnvironmentVariableRuleOptions> optionsFactory)
        => Rule.FromProvider<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions()
        );
}
