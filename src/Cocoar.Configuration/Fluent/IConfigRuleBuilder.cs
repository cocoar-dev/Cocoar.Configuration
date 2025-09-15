namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Marker for fluent builders that can produce a ConfigRule.
/// </summary>
public interface IConfigRuleBuilder
{
    ConfigRule Build();

    /// <summary>
    /// Builds multiple ConfigRules when multiple registrations are configured (e.g., different service lifetimes).
    /// </summary>
    IEnumerable<ConfigRule> BuildRules();
}
