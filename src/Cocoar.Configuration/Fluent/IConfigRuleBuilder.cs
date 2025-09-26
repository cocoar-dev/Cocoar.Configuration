using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Marker for fluent builders that can produce a ConfigRule.
/// </summary>
public interface IConfigRuleBuilder
{
    ConfigRule Build();
}
