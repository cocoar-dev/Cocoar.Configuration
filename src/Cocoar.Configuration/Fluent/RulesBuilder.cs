using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Builder for creating configuration rules with a fluent API.
/// Start with For&lt;T&gt;() to create type-safe rules.
/// </summary>
public sealed class RulesBuilder
{
    /// <summary>
    /// Start a type-safe rule for configuration type T.
    /// </summary>
    /// <typeparam name="T">The configuration type this rule will populate.</typeparam>
    /// <returns>A typed rule builder for specifying the configuration source.</returns>
#pragma warning disable CA1822 // Mark members as static - intentionally instance method for fluent API consistency
    public TypedRuleBuilder<T> For<T>() => new();
#pragma warning restore CA1822
}

/// <summary>
/// Extension methods for advanced provider scenarios on TypedRuleBuilder.
/// </summary>
public static class TypedRuleBuilderExtensions
{
    /// <summary>
    /// Creates a rule using a generic provider with custom options factories.
    /// For advanced scenarios where you need full control over provider instantiation.
    /// </summary>
    public static ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> FromProvider<T, TProvider, TInstanceOptions, TQueryOptions>(
        this TypedRuleBuilder<T> builder,
        Func<IConfigurationAccessor, TInstanceOptions> instanceOptions,
        Func<IConfigurationAccessor, TQueryOptions> queryOptions)
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
        => new(instanceOptions, queryOptions, typeof(T));
}
