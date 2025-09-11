using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers.StaticJsonProvider;

public static class RulesExtensions
{
    // Static provider convenience: seed a type with a factory
    public static ProviderRuleBuilder<
        StaticJsonProvider,
        StaticJsonProviderOptions,
        StaticJsonProviderQueryOptions
    > Static<T>(this Rule.Dsl dsl, Func<ConfigManager, T> factory, string? targetPath = null)
    {
        return new(
            cm => new StaticJsonProviderOptions(System.Text.Json.JsonSerializer.SerializeToElement(factory(cm)!)),
            _ => new StaticJsonProviderQueryOptions(targetPath)
        );
    }
}
