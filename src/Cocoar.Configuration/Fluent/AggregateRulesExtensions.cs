using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Extension methods for creating aggregate rules. Only available on <see cref="TypedRuleBuilder{T}"/>,
/// not on <see cref="TypedProviderBuilder{T}"/>, preventing recursive nesting inside aggregate lambdas.
/// </summary>
public static class AggregateRulesExtensions
{
    /// <summary>
    /// Groups multiple provider rules into a logical aggregate.
    /// Sub-rules are merged in order (last-write-wins) and share health/diagnostics grouping.
    /// </summary>
    /// <param name="builder">The typed rule builder.</param>
    /// <param name="subRules">
    /// A function that receives a <see cref="TypedProviderBuilder{T}"/> (no Aggregate/FromFiles available)
    /// and returns the sub-rules to aggregate.
    /// </param>
    /// <returns>An aggregate rule builder for further configuration.</returns>
    public static AggregateRuleBuilder<T> Aggregate<T>(
        this TypedRuleBuilder<T> builder,
        Func<TypedProviderBuilder<T>, ConfigRule[]> subRules)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(subRules);
        var providerBuilder = new TypedProviderBuilder<T>();
        var rules = subRules(providerBuilder);
        return new AggregateRuleBuilder<T>(rules);
    }

    /// <summary>
    /// Creates layered file rules where each subsequent file overrides values from earlier files.
    /// Files that don't exist are silently skipped (optional by default).
    /// Equivalent to calling <see cref="Aggregate{T}"/> with multiple FromFile sub-rules.
    /// </summary>
    /// <param name="builder">The typed rule builder.</param>
    /// <param name="filePaths">File paths in merge order (first = base, subsequent = overlays).</param>
    /// <returns>An aggregate rule builder for further configuration.</returns>
    public static AggregateRuleBuilder<T> FromFiles<T>(
        this TypedRuleBuilder<T> builder,
        params string[] filePaths)
        where T : class
    {
        if (filePaths.Length == 0)
            throw new ArgumentException("At least one file path is required.", nameof(filePaths));

        var providerBuilder = new TypedProviderBuilder<T>();
        var rules = filePaths
            .Select(path => (ConfigRule)providerBuilder.FromFile(path))
            .ToArray();

        return new AggregateRuleBuilder<T>(rules);
    }
}
