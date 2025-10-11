using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Builder for creating configuration rules with a fluent API.
/// Use extension methods to add provider-specific rules (File, Environment, Static, etc.).
/// </summary>
public sealed class RulesBuilder
{
    /// <summary>
    /// Creates a rule using a generic provider with custom options factories.
    /// </summary>
    public ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> FromProvider<TProvider, TInstanceOptions, TQueryOptions>(
        Func<IConfigurationAccessor, TInstanceOptions> instanceOptions,
        Func<IConfigurationAccessor, TQueryOptions> queryOptions)
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
        => new(instanceOptions, queryOptions);
}
