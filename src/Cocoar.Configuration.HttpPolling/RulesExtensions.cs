using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.HttpPolling;

public static class RulesExtensions
{
    public static ProviderRuleBuilder<HttpPollingProvider, HttpPollingProviderOptions, HttpPollingProviderQueryOptions>
        HttpPolling(this Rule.Dsl _, Func<IConfigurationAccessor, HttpPollingRuleOptions> optionsFactory)
        => Rule.FromProvider<HttpPollingProvider, HttpPollingProviderOptions, HttpPollingProviderQueryOptions>(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions()
        );
}
