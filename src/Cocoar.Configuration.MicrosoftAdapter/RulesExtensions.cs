using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.MicrosoftAdapter;

public static class RulesExtensions
{
    public static Fluent.MicrosoftRuleBuilder FromMicrosoftSource(this Rules.Dsl _, Func<ConfigManager, MicrosoftConfigurationSourceRuleOptions> optionsFactory)
        => new(optionsFactory);
}
