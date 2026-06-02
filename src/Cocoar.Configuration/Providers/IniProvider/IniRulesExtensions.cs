using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class IniRulesExtensions
{
    /// <summary>
    /// Creates a configuration rule that reads an <c>.ini</c> file, watched for changes. Set
    /// <paramref name="followSymlinks"/> to read symlinked files and detect atomic symlink-target swaps
    /// (e.g. Kubernetes ConfigMap mounts).
    /// </summary>
    public static ProviderRuleBuilder<IniProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromIniFile<T>(this TypedProviderBuilder<T> builder, string filePath, bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToQueryOptions(),
            typeof(T)
        );

    /// <summary>
    /// Creates an <c>.ini</c> rule from a config-aware path — e.g. a per-tenant path
    /// <c>a => $"tenants/{a.Tenant}/config.ini"</c>. The path is resolved from the accessor on each recompute.
    /// </summary>
    public static ProviderRuleBuilder<IniProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromIniFile<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, string> pathFactory, bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToQueryOptions(),
            typeof(T)
        );
}
