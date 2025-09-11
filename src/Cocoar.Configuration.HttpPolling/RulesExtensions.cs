using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.HttpPolling;

public static class RulesExtensions
{
    public static HttpRuleBuilder HttpPolling(this Rule.Dsl _, Func<ConfigManager, HttpPollingRuleOptions> optionsFactory)
        => new(optionsFactory);
}
