using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class StaticRulesExtensions
{
    /// <summary>
    /// Creates a static configuration rule from a factory function.
    /// </summary>
    public static ProviderRuleBuilder<
        StaticJsonProvider,
        StaticJsonProviderOptions,
        StaticJsonProviderQueryOptions
    > Static<T>(this RulesBuilder builder, Func<IConfigurationAccessor, T> factory)
    {
        return new(
            cm => new(JsonSerializer.SerializeToElement(factory(cm)!)),
            _ => new()
        );
    }

    /// <summary>
    /// Creates a static configuration rule from a JSON string.
    /// </summary>
    public static ProviderRuleBuilder<
        StaticJsonProvider,
        StaticJsonProviderOptions,
        StaticJsonProviderQueryOptions
    > StaticJson(this RulesBuilder builder, string jsonString)
    {
        using var document = JsonDocument.Parse(jsonString);
        var jsonElement = document.RootElement.Clone();
        
        return new(
            _ => new(jsonElement),
            _ => new()
        );
    }
}
