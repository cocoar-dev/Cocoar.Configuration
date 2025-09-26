using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Core;

internal class ConfigurationInitializer(
    List<ConfigRule> rules,
    List<RuleManager> ruleManagers,
    ProviderRegistry providerRegistry,
    ConfigurationOrchestrator orchestrator,
    ChangeSubscriptionManager subscriptionManager,
    ConfigurationHealthTracker healthTracker,
    ILogger logger,
    int debounceMs)
{
    public void Initialize(IConfigurationAccessor configAccessor, Action<int> scheduleRecompute)
    {
        ConfigurationAnalyzer.AnalyzeDependencies(rules, logger);

        ruleManagers.Clear();
        ruleManagers.AddRange(rules.Select(rule => new RuleManager(rule, logger, providerRegistry)));

        try
        {
            orchestrator.RecomputeAllConfigurationsSafe(ruleManagers, configAccessor);
            RebuildSubscriptions(scheduleRecompute);
            healthTracker.ReportSuccessfulRecompute(0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ConfigManager initialization failed");
            healthTracker.ReportFailedRecompute(0, ex);
            throw;
        }
    }

    private void RebuildSubscriptions(Action<int> scheduleRecompute)
    {
        subscriptionManager.CreateSubscriptions(ruleManagers, scheduleRecompute, debounceMs);
    }
}
