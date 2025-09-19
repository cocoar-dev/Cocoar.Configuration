using System.Text.Json;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class StaticRulesExtensions
{
    /// <summary>
    /// Creates a static configuration rule using a factory function to generate instances.
    /// The instances are serialized to JSON internally.
    /// </summary>
    /// <typeparam name="T">The type of object created by the factory.</typeparam>
    /// <param name="dsl">The rule DSL.</param>
    /// <param name="factory">Factory function that creates instances based on ConfigManager.</param>
    /// <returns>A provider rule builder for further configuration.</returns>
    public static ProviderRuleBuilder<
        StaticJsonProvider,
        StaticJsonProviderOptions,
        StaticJsonProviderQueryOptions
    > Static<T>(this Rule.Dsl dsl, Func<ConfigManager, T> factory)
    {
        return new(
            cm => new StaticJsonProviderOptions(JsonSerializer.SerializeToElement(factory(cm)!)),
            _ => new StaticJsonProviderQueryOptions()
        );
    }

    /// <summary>
    /// Creates a static configuration rule from a JSON string.
    /// </summary>
    /// <param name="dsl">The rule DSL.</param>
    /// <param name="jsonString">The JSON string containing configuration data.</param>
    /// <returns>A provider rule builder for further configuration.</returns>
    /// <exception cref="JsonException">Thrown when the JSON string is invalid.</exception>
    public static ProviderRuleBuilder<
        StaticJsonProvider,
        StaticJsonProviderOptions,
        StaticJsonProviderQueryOptions
    > StaticJson(this Rule.Dsl dsl, string jsonString)
    {
        // Parse and clone the JsonElement to avoid lifetime issues  
        using var document = JsonDocument.Parse(jsonString);
        var jsonElement = document.RootElement.Clone();
        
        return new(
            _ => new StaticJsonProviderOptions(jsonElement),
            _ => new StaticJsonProviderQueryOptions()
        );
    }
}
