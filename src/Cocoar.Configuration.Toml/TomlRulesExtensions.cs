using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Toml;

public static class TomlRulesExtensions
{
    /// <summary>
    /// Creates a configuration rule that reads a <c>.toml</c> file, watched for changes. Set
    /// <paramref name="followSymlinks"/> to read symlinked files and detect atomic symlink-target swaps
    /// (e.g. Kubernetes ConfigMap mounts).
    /// </summary>
    public static ProviderRuleBuilder<TomlFileProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromTomlFile<T>(this TypedProviderBuilder<T> builder, string filePath, bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToQueryOptions(),
            typeof(T)
        );

    /// <summary>
    /// Creates a TOML rule from a config-aware path — e.g. a per-tenant path
    /// <c>a => $"tenants/{a.Tenant}/config.toml"</c>. The path is resolved from the accessor on each recompute.
    /// </summary>
    public static ProviderRuleBuilder<TomlFileProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromTomlFile<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, string> pathFactory, bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToQueryOptions(),
            typeof(T)
        );
}
