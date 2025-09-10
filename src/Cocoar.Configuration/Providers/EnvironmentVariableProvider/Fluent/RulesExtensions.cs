using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Fluent.ProviderOptions;
using Cocoar.Configuration.Fluent.Providers;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;

public static class RulesExtensions
{
    public static EnvironmentRuleBuilder FromEnvironment(this Rules.Dsl _, Func<ConfigManager, EnvironmentVariableRuleOptions> optionsFactory)
        => new(optionsFactory);
}
