using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Yaml;

public static class YamlRulesExtensions
{
    /// <summary>
    /// Creates a configuration rule that reads a YAML file (<c>.yaml</c>/<c>.yml</c>), watched for changes.
    /// </summary>
    public static ProviderRuleBuilder<YamlFileProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromYamlFile<T>(this TypedProviderBuilder<T> builder, string filePath)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(filePath).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(filePath).ToQueryOptions(),
            typeof(T)
        );

    /// <summary>
    /// Creates a YAML rule from a config-aware path — e.g. a per-tenant path
    /// <c>a => $"tenants/{a.Tenant}/config.yaml"</c>. The path is resolved from the accessor on each recompute.
    /// </summary>
    public static ProviderRuleBuilder<YamlFileProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>
        FromYamlFile<T>(this TypedProviderBuilder<T> builder, Func<IConfigurationAccessor, string> pathFactory)
        where T : class
        => new(
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm)).ToProviderOptions(),
            cm => FileSourceRuleOptions.FromFilePath(pathFactory(cm)).ToQueryOptions(),
            typeof(T)
        );
}
