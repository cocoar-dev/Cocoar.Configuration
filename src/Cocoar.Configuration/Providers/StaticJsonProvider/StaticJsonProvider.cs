using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.StaticJsonProvider;

/// <summary>
/// A configuration provider that serves static configuration data.
/// 
/// Despite its name, this provider supports two primary use cases:
/// 1. Serving pre-created JSON elements (true static JSON)
/// 2. Serving instances created by factory functions (serialized to JSON internally)
/// 
/// This provider does not support dynamic updates - it returns the same configuration
/// data throughout the application's lifetime.
/// </summary>
public sealed class StaticJsonProvider(StaticJsonProviderOptions options)
    : ConfigurationProvider<StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(options)
{
    /// <summary>
    /// Fetches the static configuration data.
    /// </summary>
    /// <param name="query">Query options (not used for static data).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The static JSON configuration element.</returns>
    public override Task<JsonElement> FetchConfigurationAsync(StaticJsonProviderQueryOptions query,
        CancellationToken ct = default)
    {
        var json = ProviderOptions.Value.ValueKind == JsonValueKind.Undefined
            ? JsonDocument.Parse("{}").RootElement
            : ProviderOptions.Value;
        return Task.FromResult(json);
    }

    /// <summary>
    /// Returns an empty observable as static configuration never changes.
    /// </summary>
    /// <param name="queryOptions">Query options (not used for static data).</param>
    /// <returns>An empty observable sequence.</returns>
    public override IObservable<JsonElement> Changes(StaticJsonProviderQueryOptions queryOptions)
        => Observable.Empty<JsonElement>();

    /// <summary>
    /// Creates a configuration rule from a JsonElement.
    /// </summary>
    /// <typeparam name="TConfigType">The type of configuration object to bind to.</typeparam>
    /// <param name="value">The JSON element containing the configuration data.</param>
    /// <param name="useWhen">Optional condition for when this rule should be used.</param>
    /// <param name="required">Whether this configuration is required.</param>
    /// <returns>A configured rule for the specified type.</returns>
    public static ConfigRule CreateRule<TConfigType>(JsonElement value, Func<bool>? useWhen = null,
        bool required = false)
    {
        var opts = new ConfigRuleOptions(Required: required, UseWhen: useWhen);
        return ConfigRule.Create<StaticJsonProvider, StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(
            _ => new StaticJsonProviderOptions(value),
            _ => new StaticJsonProviderQueryOptions(),
            typeof(TConfigType),
            opts);
    }

    /// <summary>
    /// Creates a configuration rule from a JSON string.
    /// </summary>
    /// <typeparam name="TConfigType">The type of configuration object to bind to.</typeparam>
    /// <param name="jsonString">The JSON string containing the configuration data.</param>
    /// <param name="useWhen">Optional condition for when this rule should be used.</param>
    /// <param name="required">Whether this configuration is required.</param>
    /// <returns>A configured rule for the specified type.</returns>
    /// <exception cref="JsonException">Thrown when the JSON string is invalid.</exception>
    public static ConfigRule CreateRule<TConfigType>(string jsonString, Func<bool>? useWhen = null,
        bool required = false)
    {
        // Parse and clone the JsonElement to avoid lifetime issues
        using var document = JsonDocument.Parse(jsonString);
        var jsonElement = document.RootElement.Clone();
        return CreateRule<TConfigType>(jsonElement, useWhen, required);
    }
}
