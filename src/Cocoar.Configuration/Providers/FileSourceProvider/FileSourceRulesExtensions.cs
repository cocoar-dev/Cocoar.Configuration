using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class FileSourceRulesExtensions
{
    /// <summary>
    /// Creates a file-based configuration rule from a file path.
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromFile<T>(this TypedProviderBuilder<T> builder, string filePath)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(filePath).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(filePath).ToQueryOptions(),
            typeof(T)
        );

    /// <summary>
    /// Creates a file-based configuration rule with custom options.
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromFile<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, FileSourceRuleOptions> optionsFactory)
        where T : class
        => new(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions(),
            typeof(T)
        );
}
