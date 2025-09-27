using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class FileSourceRulesExtensions
{
    /// <summary>
    /// Creates a file-based configuration rule with custom options.
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        File(this RulesBuilder builder, Func<IConfigurationAccessor, FileSourceRuleOptions> optionsFactory)
        => builder.FromProvider<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions()
        );

    /// <summary>
    /// Creates a file-based configuration rule from a file path.
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        File(this RulesBuilder builder, string filePath)
        => builder.File(_ => FileSourceRuleOptions.FromFilePath(filePath));
}
