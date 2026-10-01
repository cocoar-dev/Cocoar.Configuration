using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// Adds ConfigHub delivery sources to typed configuration rules.
/// </summary>
public static class ConfigHubRulesExtensions
{
    /// <summary>
    /// Adds a ConfigHub-backed configuration layer. JSON snapshots remain the source of truth;
    /// SSE is used only to invalidate the current snapshot.
    /// </summary>
    /// <param name="builder">The typed rule builder.</param>
    /// <param name="url">Absolute HTTP or HTTPS ConfigHub delivery URL.</param>
    /// <param name="deliveryToken">Bearer token issued for the delivery endpoint.</param>
    /// <param name="fallbackPollInterval">Optional interval for conditional safety-net polling.</param>
    /// <param name="sseReadIdleTimeout">Optional maximum idle time before reconnecting the SSE stream.</param>
    /// <param name="handler">Optional caller-owned HTTP handler. The provider does not dispose it.</param>
    public static ProviderRuleBuilder<ConfigHubProvider, ConfigHubProviderOptions, ConfigHubProviderQueryOptions>
        FromConfigHub<T>(
            this TypedProviderBuilder<T> builder,
            string url,
            string deliveryToken,
            TimeSpan? fallbackPollInterval = null,
            TimeSpan? sseReadIdleTimeout = null,
            HttpMessageHandler? handler = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ConfigHubRuleOptions(
            url,
            deliveryToken,
            fallbackPollInterval,
            sseReadIdleTimeout,
            handler);

        return new(
            _ => options.ToProviderOptions(),
            _ => options.ToQueryOptions(),
            typeof(T));
    }

    /// <summary>
    /// Adds a ConfigHub-backed configuration layer whose endpoint or token depends on earlier rules.
    /// </summary>
    /// <param name="builder">The typed rule builder.</param>
    /// <param name="optionsFactory">Builds ConfigHub options from the current configuration pass.</param>
    public static ProviderRuleBuilder<ConfigHubProvider, ConfigHubProviderOptions, ConfigHubProviderQueryOptions>
        FromConfigHub<T>(
            this TypedProviderBuilder<T> builder,
            Func<IConfigurationAccessor, ConfigHubRuleOptions> optionsFactory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        return new(
            accessor => optionsFactory(accessor).ToProviderOptions(),
            accessor => optionsFactory(accessor).ToQueryOptions(),
            typeof(T));
    }
}
