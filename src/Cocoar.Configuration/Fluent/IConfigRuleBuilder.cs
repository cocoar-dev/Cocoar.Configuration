namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Marker for fluent builders that can produce a ConfigRule.
/// </summary>
public interface IConfigRuleBuilder
{
    ConfigRule Build();

    /// <summary>
    /// Builds the rule sequence (currently always a single rule; retained for API stability).
    /// </summary>
    IEnumerable<ConfigRule> BuildRules();
}
