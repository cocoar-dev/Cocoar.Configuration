using System.Diagnostics.Metrics;
using Cocoar.Configuration.Diagnostics;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Lightweight health tracker that derives status from rule manager outcomes.
/// AggregateRuleManagers report their own LastOutcome — no special group logic needed.
/// </summary>
internal sealed class ConfigurationHealthTracker
{
    private readonly List<IRuleManager> _ruleManagers;
    private readonly IFlagsHealthSource? _flagsHealthSource;
    private readonly ObservableGauge<int> _statusGauge;

    /// <summary>
    /// Immutable snapshot swapped atomically so concurrent readers never see
    /// a mismatched (status, description) pair.
    /// </summary>
    private volatile HealthSnapshot _snapshot = new(HealthStatus.Unknown, "Not yet initialized");

    internal sealed record HealthSnapshot(HealthStatus Status, string Description);

    public ConfigurationHealthTracker(
        List<IRuleManager> ruleManagers,
        IFlagsHealthSource? flagsHealthSource = null)
    {
        _ruleManagers = ruleManagers;
        _flagsHealthSource = flagsHealthSource;
        _statusGauge = CocoarMetrics.Instance.CreateObservableGauge(
            "cocoar.config.health.status",
            () => (int)_snapshot.Status,
            description: "Current health status (1=Healthy, 2=Degraded, 3=Unhealthy)");
    }

    public HealthStatus Status => _snapshot.Status;
    public string Description => _snapshot.Description;

    public void UpdateAfterRecompute()
    {
        var requiredFailed = 0;
        var optionalFailed = 0;
        var anyUnknown = false;
        var total = _ruleManagers.Count;

        for (var i = 0; i < _ruleManagers.Count; i++)
        {
            var rm = _ruleManagers[i];
            switch (rm.LastOutcome)
            {
                case RuleExecutionOutcome.Failed:
                    if (rm.Required) requiredFailed++;
                    else optionalFailed++;
                    break;
                case RuleExecutionOutcome.Unknown:
                    anyUnknown = true;
                    break;
            }
        }

        var hasExpiredFlags = _flagsHealthSource?.HasExpiredFlags() == true;

        if (requiredFailed > 0)
        {
            _snapshot = new(HealthStatus.Unhealthy, $"{requiredFailed} required rule(s) failed");
        }
        else if (optionalFailed > 0 || hasExpiredFlags)
        {
            var parts = new List<string>();
            if (optionalFailed > 0) parts.Add($"{optionalFailed} optional rule(s) failed");
            if (hasExpiredFlags) parts.Add("expired feature flags detected");
            _snapshot = new(HealthStatus.Degraded, string.Join("; ", parts));
        }
        else if (anyUnknown || total == 0)
        {
            _snapshot = new(HealthStatus.Unknown,
                total == 0 ? "No rules configured" : "Some rules have not yet been evaluated");
        }
        else
        {
            _snapshot = new(HealthStatus.Healthy, "All rules healthy");
        }
    }
}
