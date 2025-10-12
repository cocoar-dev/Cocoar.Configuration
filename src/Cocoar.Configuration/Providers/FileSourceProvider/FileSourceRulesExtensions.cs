using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class FileSourceRulesExtensions
{
    /// <summary>
    /// Creates a file-based configuration rule from a file path.
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromFile<T>(this TypedRuleBuilder<T> builder, string filePath)
        => new(
            cm => FileSourceRuleOptions.FromFilePath(filePath).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(filePath).ToQueryOptions(),
            typeof(T)
        );

    /// <summary>
    /// Creates a file-based configuration rule with custom options.
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromFile<T>(this TypedRuleBuilder<T> builder, Func<IConfigurationAccessor, FileSourceRuleOptions> optionsFactory)
        => new(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions(),
            typeof(T)
        );
}
