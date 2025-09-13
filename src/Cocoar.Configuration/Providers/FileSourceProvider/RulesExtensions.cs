using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers.FileSourceProvider;

public static class RulesExtensions
{
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions> File(this Rule.Dsl _, Func<ConfigManager, FileSourceRuleOptions> optionsFactory)
        => Rule.FromProvider<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions()
        );
}
