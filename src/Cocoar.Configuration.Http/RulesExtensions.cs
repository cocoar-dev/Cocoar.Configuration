using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Http;

public static class RulesExtensions
{
    /// <summary>
    /// Creates an HTTP configuration rule with simple parameters.
    /// This is the most common overload for straightforward HTTP configuration sources.
    /// </summary>
    /// <param name="builder">The typed rule builder.</param>
    /// <param name="url">The URL (absolute or relative to base address) to fetch configuration from.</param>
    /// <param name="pollInterval">Optional polling interval. When null and SSE is disabled, configuration is fetched once.</param>
    /// <param name="serverSentEvents">When true, uses Server-Sent Events for live updates.</param>
    /// <param name="fallbackPollInterval">When SSE is enabled, falls back to polling at this interval on sustained SSE failure.</param>
    /// <param name="headers">Optional HTTP headers sent with every request.</param>
    public static ProviderRuleBuilder<HttpProvider, HttpProviderOptions, HttpProviderQueryOptions>
        FromHttp<T>(this TypedRuleBuilder<T> builder,
            string url,
            TimeSpan? pollInterval = null,
            bool serverSentEvents = false,
            TimeSpan? fallbackPollInterval = null,
            IReadOnlyDictionary<string, string>? headers = null,
            HttpMessageHandler? handler = null)
        where T : class
    {
        var ruleOptions = new HttpRuleOptions(url, pollInterval, serverSentEvents, fallbackPollInterval, headers, handler);
        return new(
            _ => ruleOptions.ToProviderOptions(),
            _ => ruleOptions.ToQueryOptions(),
            typeof(T)
        );
    }

    /// <summary>
    /// Creates an HTTP configuration rule with full options via accessor.
    /// Use this overload when the URL or options depend on other configuration values.
    /// </summary>
    /// <param name="builder">The typed rule builder.</param>
    /// <param name="optionsFactory">Factory that receives the current configuration accessor and returns HTTP rule options.</param>
    public static ProviderRuleBuilder<HttpProvider, HttpProviderOptions, HttpProviderQueryOptions>
        FromHttp<T>(this TypedRuleBuilder<T> builder,
            Func<IConfigurationAccessor, HttpRuleOptions> optionsFactory)
        where T : class
        => new(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions(),
            typeof(T)
        );
}
