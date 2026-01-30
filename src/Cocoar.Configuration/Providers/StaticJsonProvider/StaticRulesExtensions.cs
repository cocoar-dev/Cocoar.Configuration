using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Testing;

namespace Cocoar.Configuration.Providers;

public static class StaticRulesExtensions
{
    /// <summary>
    /// Creates a static configuration rule from a JSON string.
    /// </summary>
    public static ProviderRuleBuilder<
        StaticJsonProvider,
        StaticJsonProviderOptions,
        StaticJsonProviderQueryOptions
    > FromStaticJson<T>(this TypedRuleBuilder<T> builder, string jsonString)
    {
        using var document = JsonDocument.Parse(jsonString);
        var jsonElement = document.RootElement.Clone();
        
        return new(
            _ => new(jsonElement),
            _ => new(),
            typeof(T)
        );
    }

    /// <summary>
    /// Creates a static configuration rule from a factory function.
    /// </summary>
    public static ProviderRuleBuilder<
        StaticJsonProvider,
        StaticJsonProviderOptions,
        StaticJsonProviderQueryOptions
    > FromStatic<T>(this TypedRuleBuilder<T> builder, Func<IConfigurationAccessor, T> factory)
    {
        return new(
            cm =>
            {
                var obj = factory(cm)!;
                var options = GetSerializerOptions();
                return new(JsonSerializer.SerializeToElement(obj, options));
            },
            _ => new(),
            typeof(T)
        );
    }

    private static JsonSerializerOptions? GetSerializerOptions()
    {
        // Only use custom serialization in test context with registered options
        if (CocoarTestConfiguration.Current != null)
        {
            return CocoarTestConfiguration.TestSerializerOptions;
        }
        return null;  // Use default serialization
    }
}
