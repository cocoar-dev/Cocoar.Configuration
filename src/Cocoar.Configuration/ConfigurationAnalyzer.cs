using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

/// <summary>
/// Provides basic analysis and validation for configuration rules.
/// </summary>
internal static class ConfigurationAnalyzer
{
    /// <summary>
    /// Performs basic dependency analysis on configuration rules.
    /// Warns about potential missing dependencies during rule setup.
    /// </summary>
    /// <param name="rules">The rules to analyze</param>
    /// <param name="logger">Logger for warnings</param>
    public static void AnalyzeDependencies(IEnumerable<ConfigRule> rules, ILogger logger)
    {
        var rulesList = rules.ToList();
        var rulesByType = new Dictionary<Type, List<ConfigRule>>();

        // Collect all types that rules provide
        foreach (var rule in rulesList)
        {
            // Group rules by their target type
            var targetType = rule.Registration.ContractType ?? rule.Registration.ConcreteType;
            if (!rulesByType.ContainsKey(targetType))
                rulesByType[targetType] = new List<ConfigRule>();
            rulesByType[targetType].Add(rule);
        }

        // Check for common issues
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
            logger.LogInformation("Configuration has {RequiredCount} required rules and {OptionalCount} optional rules. " +
                "Required rules will fail the entire recompute on error.", 
                requiredRules.Count, optionalRules.Count);
        }

        // Check for factory-based rules (these might have dynamic dependencies)
        var factoryRules = rules.Where(r => HasComplexFactory(r)).ToList();
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

    private static bool HasComplexFactory(ConfigRule rule)
    {
        // Simple heuristic: assume all factory-based rules could have dependencies
        // In practice you might want more sophisticated analysis
        return true;
    }
}
