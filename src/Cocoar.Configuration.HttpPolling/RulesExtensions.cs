using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.HttpPolling;

public static class RulesExtensions
{
    /// <summary>
    /// Creates an HTTP polling configuration rule with custom options.
    /// </summary>
    public static ProviderRuleBuilder<HttpPollingProvider, HttpPollingProviderOptions, HttpPollingProviderQueryOptions>
        HttpPolling(this RulesBuilder builder, Func<IConfigurationAccessor, HttpPollingRuleOptions> optionsFactory)
        => builder.FromProvider<HttpPollingProvider, HttpPollingProviderOptions, HttpPollingProviderQueryOptions>(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions()
        );
}
