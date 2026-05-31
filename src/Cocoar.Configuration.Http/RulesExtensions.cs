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
        FromHttp<T>(this TypedProviderBuilder<T> builder,
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
        FromHttp<T>(this TypedProviderBuilder<T> builder,
            Func<IConfigurationAccessor, HttpRuleOptions> optionsFactory)
        where T : class
        => new(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions(),
            typeof(T)
        );

    /// <summary>
    /// Creates a <b>service-backed</b> (Layer-2, ADR-006) HTTP configuration rule whose underlying
    /// <see cref="HttpClient"/> is sourced from the application container — typically via
    /// <c>IHttpClientFactory</c>, gaining handler pooling/rotation and <c>AddHttpClient</c> policies (Polly).
    /// </summary>
    /// <remarks>
    /// Only valid inside <c>UseServiceBackedConfiguration(...)</c> (it needs the container's
    /// <see cref="IServiceProvider"/>). The rule stays dormant until the host starts; on activation it runs and
    /// merges over the Layer-1 base, and every live <c>IReactiveConfig&lt;T&gt;</c> view receives the update.
    /// The factory is invoked lazily, only when the provider is (re)built, never before the container exists.
    /// </remarks>
    /// <param name="builder">The typed provider builder.</param>
    /// <param name="clientFactory">Factory receiving the root <see cref="IServiceProvider"/> and the current
    /// <see cref="IConfigurationAccessor"/> (its <c>Tenant</c> is set in a tenant pipeline) and returning the
    /// <see cref="HttpClient"/> to use, e.g. <c>(sp, a) =&gt; sp.GetRequiredService&lt;IHttpClientFactory&gt;().CreateClient("cocoar-config")</c>.</param>
    /// <param name="url">The URL (absolute or relative to the client's base address) to fetch configuration from.</param>
    /// <param name="pollInterval">Optional polling interval. When null and SSE is disabled, configuration is fetched once.</param>
    /// <param name="serverSentEvents">When true, uses Server-Sent Events for live updates.</param>
    /// <param name="fallbackPollInterval">When SSE is enabled, falls back to polling at this interval on sustained SSE failure.</param>
    /// <param name="headers">Optional HTTP headers sent with every request.</param>
    public static ProviderRuleBuilder<HttpProvider, HttpProviderOptions, HttpProviderQueryOptions>
        FromHttp<T>(this ServiceBackedProviderBuilder<T> builder,
            Func<IServiceProvider, IConfigurationAccessor, HttpClient> clientFactory,
            string url,
            TimeSpan? pollInterval = null,
            bool serverSentEvents = false,
            TimeSpan? fallbackPollInterval = null,
            IReadOnlyDictionary<string, string>? headers = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(clientFactory);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("url is required", nameof(url));
        }

        // ServiceBacked hands us sp as a parameter invoked lazily at recompute time, and gates the rule
        // automatically — no manual deferral or activation gate to get wrong.
        return builder.ServiceBacked<HttpProvider, HttpProviderOptions, HttpProviderQueryOptions>(
            (sp, accessor) => new HttpProviderOptions(
                pollInterval,
                serverSentEvents,
                fallbackPollInterval,
                clientFactory: () => clientFactory(sp, accessor)),
            _ => new HttpProviderQueryOptions(url, headers));
    }
}
