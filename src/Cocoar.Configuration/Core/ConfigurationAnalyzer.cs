using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Core;

internal static class ConfigurationAnalyzer
{
    public static void AnalyzeDependencies(IEnumerable<ConfigRule> rules, ILogger logger)
    {
        var rulesList = rules.ToList();
        var rulesByType = new Dictionary<Type, List<ConfigRule>>();

        foreach (var rule in rulesList)
        {
            var targetType = rule.ConcreteType;
            if (!rulesByType.ContainsKey(targetType))
            {
                rulesByType[targetType] = [];
            }

            rulesByType[targetType].Add(rule);
        }

        AnalyzeRuleOrdering(rulesList, logger);
        AnalyzeRequiredRules(rulesList, logger);
        LogRuleSummary(rulesByType, logger);
    }

    private static void AnalyzeRuleOrdering(List<ConfigRule> rules, ILogger logger)
    {
        var hasStaticProviders = false;
        var hasNonStaticProviders = false;

        foreach (var rule in rules)
        {
            if (rule.ProviderType.Name.Contains("Static"))
            {
                hasStaticProviders = true;
            }
            else
            {
                hasNonStaticProviders = true;
            }
        }

        if (hasStaticProviders && hasNonStaticProviders)
        {
            logger.LogInformation("Configuration includes both static seed rules and dynamic providers. " +
                                  "Ensure static rules come before rules that depend on them.");
        }
    }

    private static void AnalyzeRequiredRules(List<ConfigRule> rules, ILogger logger)
    {
        var requiredRules = rules.Where(r => r.Options?.Required == true).ToList();
        var optionalRules = rules.Where(r => r.Options?.Required == false).ToList();

        if (requiredRules.Any() && optionalRules.Any())
        {
            logger.LogInformation(
                "Configuration has {RequiredCount} required rules and {OptionalCount} optional rules. " +
                "Required rules will fail the entire recompute on error.",
                requiredRules.Count, optionalRules.Count);
        }

        var factoryRules = rules.ToList();
        if (factoryRules.Any())
        {
            logger.LogInformation("Found {FactoryRuleCount} rules with dynamic factories. " +
                                  "Ensure dependent types are produced by earlier rules to avoid GetRequiredConfig exceptions.",
                factoryRules.Count);
        }
    }

    private static void LogRuleSummary(Dictionary<Type, List<ConfigRule>> rulesByType, ILogger logger)
    {
        logger.LogDebug("Configuration rule summary:");
        foreach (var (type, rules) in rulesByType)
        {
            logger.LogDebug("  {TypeName}: {RuleCount} rule(s) ({Providers})",
                type.Name,
                rules.Count,
                string.Join(", ", rules.Select(r => r.ProviderType.Name)));
        }
    }

}
