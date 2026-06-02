using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class DotEnvRulesExtensions
{
    /// <summary>
    /// Creates a configuration rule that reads a <c>.env</c> file (defaults to <c>.env</c> in the base directory).
    /// Set <paramref name="followSymlinks"/> to read symlinked files and detect atomic symlink-target
    /// swaps (e.g. Kubernetes ConfigMap mounts).
    /// </summary>
    public static ProviderRuleBuilder<DotEnvProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromDotEnv<T>(this TypedProviderBuilder<T> builder, string filePath = ".env", bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(filePath, followSymlinks: followSymlinks).ToQueryOptions(),
            typeof(T)
        );

    /// <summary>
    /// Creates a <c>.env</c> rule from a config-aware path — e.g. a per-tenant path
    /// <c>a => $"tenants/{a.Tenant}/.env"</c>. The path is resolved from the accessor on each recompute.
    /// </summary>
    public static ProviderRuleBuilder<DotEnvProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromDotEnv<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, string> pathFactory, bool followSymlinks = false)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm), followSymlinks: followSymlinks).ToQueryOptions(),
            typeof(T)
        );
}
