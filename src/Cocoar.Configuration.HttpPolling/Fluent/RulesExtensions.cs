using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.HttpPolling.Fluent.ProviderOptions;

namespace Cocoar.Configuration.HttpPolling.Fluent;

public static class RulesExtensions
{
    public static HttpRuleBuilder FromHttp(this Rules.Dsl _, Func<ConfigManager, HttpPollingRuleOptions> optionsFactory)
        => new(optionsFactory);
}
