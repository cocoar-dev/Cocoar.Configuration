namespace Cocoar.Configuration.Rules;

/// <summary>
/// A configuration rule that groups multiple sub-rules into a logical aggregate.
/// Sub-rules are expanded and executed individually by the engine, but share a common
/// group identity for health tracking and observability.
/// </summary>
public sealed class AggregateConfigRule : ConfigRule
{
    /// <summary>
    /// The sub-rules that make up this aggregate, in merge order.
    /// </summary>
    public IReadOnlyList<ConfigRule> SubRules { get; }

    internal AggregateConfigRule(
        IReadOnlyList<ConfigRule> subRules,
        Type concreteType,
        ConfigRuleOptions? options)
        : base(concreteType, options)
    {
        if (subRules.Count == 0)
            throw new ArgumentException("Aggregate must contain at least one sub-rule.", nameof(subRules));

        SubRules = subRules;
    }
}
