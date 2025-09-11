using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.MicrosoftAdapter;

public static class RulesExtensions
{
    public static Fluent.MicrosoftRuleBuilder MicrosoftSource(this Rule.Dsl _, Func<ConfigManager, MicrosoftConfigurationSourceRuleOptions> optionsFactory)
        => new(optionsFactory);
}
