namespace Cocoar.Configuration.Health;

/// <summary>
/// Simple health information for a single configuration rule.
/// </summary>
public readonly record struct RuleHealthInfo(
    RuleResultStatus Status,
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
    public HealthStatus State
    {
        get
        {
            if (Rules.Count == 0)
            {
                return HealthStatus.Unknown;
            }
            if (Rules.Any(r => r is { IsRequired: true, Status: RuleResultStatus.Down }))
            {
                return HealthStatus.Unhealthy;
            }
            if (Rules.Any(r => r.Status is RuleResultStatus.Unknown))
            {
                return HealthStatus.Unknown;
            }
            if (Rules.Any(r => r is { IsRequired: false, Status: RuleResultStatus.Down }))
            {
                return HealthStatus.Degraded;
            }
            return HealthStatus.Healthy;
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
