using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.MicrosoftAdapter;

public static class RulesExtensions
{
    public static
        ProviderRuleBuilder<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions,
            MicrosoftConfigurationSourceProviderQueryOptions> MicrosoftSource(this Rule.Dsl _,
            Func<ConfigManager, MicrosoftConfigurationSourceRuleOptions> optionsFactory)
        => Rule
            .FromProvider<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions,
                MicrosoftConfigurationSourceProviderQueryOptions>(
                cm => optionsFactory(cm).ToProviderOptions(),
                cm => optionsFactory(cm).ToQueryOptions()
            );
}
