using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.MicrosoftAdapter.Fluent;

public static class RulesExtensions
{
    public static MicrosoftRuleBuilder MicrosoftSource(this Rule.Dsl _, Func<ConfigManager, MicrosoftConfigurationSourceRuleOptions> optionsFactory)
        => new(optionsFactory);
}
