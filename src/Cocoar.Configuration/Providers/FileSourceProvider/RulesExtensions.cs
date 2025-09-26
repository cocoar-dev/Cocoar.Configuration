using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class FileSourceRulesExtensions
{
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        File(this Rule.Dsl _, Func<IConfigurationAccessor, FileSourceRuleOptions> optionsFactory)
        => Rule.FromProvider<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions()
        );

    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        File(this Rule.Dsl _, string filePath)
        => _.File(_ => FileSourceRuleOptions.FromFilePath(filePath));
}
