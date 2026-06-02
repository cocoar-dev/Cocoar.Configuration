using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class FileSourceRulesExtensions
{
    /// <summary>
    /// Creates a file-based configuration rule from a file path. Set <paramref name="followSymlinks"/>
    /// to read symlinked files and detect atomic symlink-target swaps (e.g. Kubernetes ConfigMap mounts).
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromFile<T>(this TypedProviderBuilder<T> builder, string filePath, bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToQueryOptions(),
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

    /// <summary>
    /// Creates a file-based configuration rule from a config-aware file path — e.g. a per-tenant path
    /// <c>a => $"tenants/{a.Tenant}/db.json"</c>. The path is resolved from the accessor on each recompute.
    /// </summary>
    public static ProviderRuleBuilder<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromFile<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, string> pathFactory, bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToQueryOptions(),
            typeof(T)
        );
}
