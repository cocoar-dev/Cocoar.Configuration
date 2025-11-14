namespace Cocoar.Configuration.Health;

/// <summary>
/// Represents the overall health status of the configuration system.
/// </summary>
public enum ConfigurationHealthStatus
{
    /// <summary>
    /// All required rules are healthy and functioning correctly.
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// Some optional rules are failing, but all required rules are healthy.
    /// </summary>
    Degraded,

    /// <summary>
    /// One or more required rules are failing.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// Configuration health is not yet determined.
    /// </summary>
    Unknown
}

/// <summary>
/// Represents the evaluation state of a configuration rule.
/// </summary>
public enum RuleEvaluationState
{
    /// <summary>
    /// Rule has not been evaluated yet.
    /// </summary>
    NotEvaluated = 0,

    /// <summary>
    /// Rule was skipped due to When predicate evaluation.
    /// </summary>
    Skipped,

    /// <summary>
    /// Rule evaluation is currently in progress.
    /// </summary>
    Evaluating,

    /// <summary>
    /// Rule was successfully evaluated.
    /// </summary>
    Success,

    /// <summary>
    /// Rule evaluation failed.
    /// </summary>
    Failed
}

/// <summary>
/// Simple health information for a single configuration rule.
/// </summary>
public readonly record struct RuleHealthInfo(
    RuleEvaluationState State,
    bool IsRequired,
    bool IsSkipped
);

/// <summary>
/// Simple health information for the entire configuration system.
/// State is automatically calculated from the rules array.
/// </summary>
public class ConfigurationHealthInfo
{
    public IReadOnlyList<RuleHealthInfo> Rules { get; }

    /// <summary>
    /// Overall health status calculated automatically from rule states.
    /// </summary>
    public ConfigurationHealthStatus State
    {
        get
        {
            if (Rules.Count == 0)
            {
                return ConfigurationHealthStatus.Unknown;
            }
            if (Rules.Any(r => r is { IsRequired: true, State: RuleEvaluationState.Failed }))
            {
                return ConfigurationHealthStatus.Unhealthy;
            }
            if (Rules.Any(r => r.State is RuleEvaluationState.Evaluating or RuleEvaluationState.NotEvaluated))
            {
                return ConfigurationHealthStatus.Unknown;
            }
            if (Rules.Any(r => r is { IsRequired: false, State: RuleEvaluationState.Failed }))
            {
                return ConfigurationHealthStatus.Degraded;
            }
            return ConfigurationHealthStatus.Healthy;
        }
    }

    /// <summary>
    /// Creates a new configuration health info with calculated state.
    /// </summary>
    /// <param name="rules">Array of rule health information</param>
    public ConfigurationHealthInfo(IReadOnlyList<RuleHealthInfo> rules)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    /// <summary>
    /// Creates a new configuration health info with calculated state.
    /// </summary>
    /// <param name="rules">Array of rule health information</param>
    public ConfigurationHealthInfo(RuleHealthInfo[] rules)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }
}
