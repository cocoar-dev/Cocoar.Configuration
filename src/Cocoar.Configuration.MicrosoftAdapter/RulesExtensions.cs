using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.MicrosoftAdapter;

public static class RulesExtensions
{
    /// <summary>
    /// Creates a Microsoft configuration source rule with custom options.
    /// </summary>
    public static
        ProviderRuleBuilder<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions,
            MicrosoftConfigurationSourceProviderQueryOptions> MicrosoftSource(this RulesBuilder builder,
            Func<IConfigurationAccessor, MicrosoftConfigurationSourceRuleOptions> optionsFactory)
        => builder
            .FromProvider<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions,
                MicrosoftConfigurationSourceProviderQueryOptions>(
                cm => optionsFactory(cm).ToProviderOptions(),
                cm => optionsFactory(cm).ToQueryOptions()
            );
}
