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
            MicrosoftConfigurationSourceProviderQueryOptions> FromMicrosoftSource<T>(this TypedRuleBuilder<T> builder,
            Func<IConfigurationAccessor, MicrosoftConfigurationSourceRuleOptions> optionsFactory)
        => new(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions(),
            typeof(T)
        );
}
