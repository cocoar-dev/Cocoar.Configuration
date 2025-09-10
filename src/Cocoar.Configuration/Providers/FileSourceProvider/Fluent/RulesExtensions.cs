using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Fluent.ProviderOptions;
using Cocoar.Configuration.Fluent.Providers;

namespace Cocoar.Configuration.Providers.FileSourceProvider.Fluent;

public static class RulesExtensions
{
    public static FileRuleBuilder FromFile(this Rules.Dsl _, Func<ConfigManager, FileSourceRuleOptions> optionsFactory)
        => new(optionsFactory);
}
