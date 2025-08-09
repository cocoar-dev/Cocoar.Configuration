using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.HttpPolling;

public static class RulesExtensions
{
    public static HttpRuleBuilder FromHttp(this Rules.Dsl _, Func<ConfigManager, HttpPollingRuleOptions> optionsFactory)
        => new(optionsFactory);
}
