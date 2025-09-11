using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.MicrosoftAdapter.Fluent;

public static class RulesExtensions
{
    public static MicrosoftRuleBuilder FromMicrosoftSource(this Rules.Dsl _, Func<ConfigManager, MicrosoftConfigurationSourceRuleOptions> optionsFactory)
        => new(optionsFactory);
}
