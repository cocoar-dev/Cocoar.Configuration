using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.HttpPolling;

public static class RulesExtensions
{
    /// <summary>
    /// Creates an HTTP polling configuration rule with custom options.
    /// </summary>
    public static ProviderRuleBuilder<HttpPollingProvider, HttpPollingProviderOptions, HttpPollingProviderQueryOptions>
        FromHttpPolling<T>(this TypedRuleBuilder<T> builder, Func<IConfigurationAccessor, HttpPollingRuleOptions> optionsFactory)
        => new(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions(),
            typeof(T)
        );
}
